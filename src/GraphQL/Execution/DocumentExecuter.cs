﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GraphQL.Execution;
using GraphQL.Language;
using GraphQL.Types;
using GraphQL.Validation;

namespace GraphQL
{
    public interface IDocumentExecuter
    {
        ExecutionResult Execute(Schema schema, string query, string operationName, Inputs inputs = null);
    }

    public class DocumentExecuter : IDocumentExecuter
    {
        private readonly IDocumentBuilder _documentBuilder;
        private readonly IDocumentValidator _documentValidator;

        public DocumentExecuter()
            : this(new AntlrDocumentBuilder(), new DocumentValidator())
        {
        }

        public DocumentExecuter(IDocumentBuilder documentBuilder, IDocumentValidator documentValidator)
        {
            _documentBuilder = documentBuilder;
            _documentValidator = documentValidator;
        }

        public ExecutionResult Execute(Schema schema, string query, string operationName, Inputs inputs = null)
        {
            var document = _documentBuilder.Build(query);
            var result = new ExecutionResult();

            var validationResult = _documentValidator.IsValid(schema, document, operationName);

            if (validationResult.IsValid)
            {
                var context = BuildExecutionContext(schema, document, operationName, inputs);

                if (context.Errors.Any())
                {
                    result.Errors = context.Errors;
                    return result;
                }

                result.Data = ExecuteOperation(context);
                result.Errors = context.Errors;
            }
            else
            {
                result.Data = null;
                result.Errors = validationResult.Errors;
            }

            return result;
        }

        public ExecutionContext BuildExecutionContext(
            Schema schema,
            Document document,
            string operationName,
            Inputs inputs)
        {
            var context = new ExecutionContext();
            context.Schema = schema;

            var operation = !string.IsNullOrWhiteSpace(operationName)
                ? document.Operations.WithName(operationName)
                : document.Operations.FirstOrDefault();

            if (operation == null)
            {
                context.Errors.Add(new ExecutionError("Unknown operation name: {0}".ToFormat(operationName)));
                return context;
            }

            context.Operation = operation;
            context.Variables = GetVariableValues(schema, operation.Variables, inputs);
            context.Fragments = document.Fragments;

            return context;
        }

        public object ExecuteOperation(ExecutionContext context)
        {
            var rootType = GetOperationRootType(context.Schema, context.Operation);
            var fields = CollectFields(context, rootType, context.Operation.Selections, null);

            return ExecuteFields(context, rootType, null, fields);
        }

        public object ExecuteFields(ExecutionContext context, ObjectGraphType rootType, object source, Dictionary<string, Fields> fields)
        {
            var result = new Dictionary<string, object>();

            fields.Apply(pair =>
            {
                result[pair.Key] = ResolveField(context, rootType, source, pair.Value);
            });

            return result;
        }

        public object ResolveField(ExecutionContext context, ObjectGraphType parentType, object source, Fields fields)
        {
            var field = fields.First();

            var fieldDefinition = GetFieldDefinition(context.Schema, parentType, field);
            if (fieldDefinition == null)
            {
                return null;
            }

            var arguments = GetArgumentValues(fieldDefinition.Arguments, field.Arguments, context.Variables);

            Func<ResolveFieldContext, object> defaultResolve = (ctx) =>
            {
                return ctx.Source != null ? GetProperyValue(ctx.Source, ctx.FieldAst.Name) : null;
            };

            try
            {
                var resolveContext = new ResolveFieldContext();
                resolveContext.FieldAst = field;
                resolveContext.FieldDefinition = fieldDefinition;
                resolveContext.Schema = context.Schema;
                resolveContext.ParentType = parentType;
                resolveContext.Arguments = arguments;
                resolveContext.Source = source;
                var resolve = fieldDefinition.Resolve ?? defaultResolve;
                var result = resolve(resolveContext);
                return CompleteValue(context, fieldDefinition.Type, fields, result);
            }
            catch (Exception exc)
            {
                context.Errors.Add(new ExecutionError("Error trying to resolve {0}.".ToFormat(field.Name), exc));
                return null;
            }
        }

        public object GetProperyValue(object obj, string propertyName)
        {
            var val = obj.GetType()
                .GetProperty(propertyName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance)
                .GetValue(obj, null);

            return val;
        }

        public object CompleteValue(ExecutionContext context, GraphType fieldType, Fields fields, object result)
        {
            if (fieldType is NonNullGraphType)
            {
                var nonNullType = fieldType as NonNullGraphType;
                var completed = CompleteValue(context, nonNullType.Type, fields, result);
                if (completed == null)
                {
                    throw new ExecutionError("Cannot return null for non-null type. Field: {0}".ToFormat(nonNullType.Name));
                }

                return completed;
            }

            if (result == null)
            {
                return null;
            }

            if (fieldType is ScalarGraphType)
            {
                var scalarType = fieldType as ScalarGraphType;
                var coercedValue = scalarType.Coerce(result);
                return coercedValue;
            }

            if (fieldType is ListGraphType)
            {
                var list = result as IEnumerable;

                if (list == null)
                {
                    throw new ExecutionError("User error: expected an IEnumerable list though did not find one.");
                }

                var listType = fieldType as ListGraphType;

                var itemType = listType.CreateType();

                var results = list.Map(item =>
                {
                    return CompleteValue(context, itemType, fields, item);
                });

                return results;
            }

            var objectType = fieldType as ObjectGraphType;

            if (fieldType is InterfaceGraphType)
            {
                var interfaceType = fieldType as InterfaceGraphType;
                objectType = interfaceType.ResolveType(result);
            }

            if (objectType == null)
            {
                return null;
            }

            var subFields = new Dictionary<string, Fields>();

            fields.Apply(field =>
            {
                subFields = CollectFields(context, objectType, field.Selections, subFields);
            });

            return ExecuteFields(context, objectType, result, subFields);
        }

        public Dictionary<string, object> GetArgumentValues(QueryArguments definitionArguments, Arguments astArguments, Variables variables)
        {
            if (definitionArguments == null || !definitionArguments.Any())
            {
                return null;
            }

            return definitionArguments.Aggregate(new Dictionary<string, object>(), (acc, arg) =>
            {
                var value = astArguments.ValueFor(arg.Name);
                acc[arg.Name] = CoerceValueAst(arg.Type, value, variables);
                return acc;
            });
        }

        public FieldType GetFieldDefinition(Schema schema, ObjectGraphType parentType, Field field)
        {
            // TODO: handle meta fields

            return parentType.Fields.FirstOrDefault(f => f.Name == field.Name);
        }

        public ObjectGraphType GetOperationRootType(Schema schema, Operation operation)
        {
            ObjectGraphType type;

            switch (operation.OperationType)
            {
                case OperationType.Query:
                    type = schema.Query;
                    break;

                case OperationType.Mutation:
                    type = schema.Mutation;
                    break;

                default:
                    throw new InvalidOperationException("Can only execute queries and mutations");
            }

            return type;
        }

        public Variables GetVariableValues(Schema schema, Variables variables, Inputs inputs)
        {
            if (inputs != null)
            {
                variables.Apply(v =>
                {
                    v.Value = GetVariableValue(schema, v, inputs[v.Name]);
                });
            }

            return variables;
        }

        public object GetVariableValue(Schema schema, Variable variable, object input)
        {
            var type = schema.FindType(variable.Type.Name);
            if (IsValidValue(type, input))
            {
                if (input == null && variable.DefaultValue != null)
                {
                    return CoerceValueAst(type, variable.DefaultValue, null);
                }

                return CoerceValue(type, input);
            }

            throw new Exception("Variable {0} expected type '{1}'.".ToFormat(variable.Name, type.Name));
        }

        public bool IsValidValue(GraphType type, object input)
        {
            if (type is NonNullGraphType)
            {
                if (input == null)
                {
                    return false;
                }

                return IsValidValue(((NonNullGraphType)type).Type, input);
            }

            if (input == null)
            {
                return true;
            }

            if (type is ListGraphType)
            {
                var listType = (ListGraphType) type;
                var list = input as IEnumerable;
                return list != null
                    ? list.All(item => IsValidValue(type, item))
                    : IsValidValue(listType, input);
            }

            if (type is ObjectGraphType)
            {
                var dict = input as Dictionary<string, object>;
                return dict != null
                    && type.Fields.All(field => IsValidValue(field.Type, dict[field.Name]));
            }

            if (type is ScalarGraphType)
            {
                var scalar = (ScalarGraphType) type;
                return scalar.Coerce(input) != null;
            }

            return false;
        }

        // TODO: combine dupliation with CoerceValueAST
        public object CoerceValue(GraphType type, object input)
        {
            if (type is NonNullGraphType)
            {
                var nonNull = type as NonNullGraphType;
                return CoerceValue(nonNull.Type, input);
            }

            if (input == null)
            {
                return null;
            }

            if (type is ListGraphType)
            {
                var listType = type as ListGraphType;
                var list = input as IEnumerable;
                return list != null
                    ? list.Map(item => CoerceValue(listType, item))
                    : new[] { input };
            }

            if (type is ObjectGraphType)
            {
                var objType = type as ObjectGraphType;
                var obj = new Dictionary<string, object>();
                var dict = (Dictionary<string, object>)input;

                objType.Fields.Apply(field =>
                {
                    var fieldValue = CoerceValue(field.Type, dict[field.Name]);
                    obj[field.Name] = fieldValue ?? field.DefaultValue;
                });
            }

            if (type is ScalarGraphType)
            {
                var scalarType = type as ScalarGraphType;
                return scalarType.Coerce(input);
            }

            return null;
        }

        // TODO: combine duplication with CoerceValue
        public object CoerceValueAst(GraphType type, object input, Variables variables)
        {
            if (type is NonNullGraphType)
            {
                var nonNull = type as NonNullGraphType;
                return CoerceValueAst(nonNull.Type, input, variables);
            }

            if (input == null)
            {
                return null;
            }

            if (input is Variable)
            {
                return variables != null
                    ? variables.ValueFor(((Variable)input).Name)
                    : null;
            }

            if (type is ListGraphType)
            {
                var listType = type as ListGraphType;
                var list = input as IEnumerable;
                return list != null
                    ? list.Map(item => CoerceValueAst(listType, item, variables))
                    : new[] { input };
            }

            if (type is ObjectGraphType)
            {
                var objType = type as ObjectGraphType;
                var obj = new Dictionary<string, object>();
                var dict = (Dictionary<string, object>)input;

                objType.Fields.Apply(field =>
                {
                    var fieldValue = CoerceValueAst(field.Type, dict[field.Name], variables);
                    obj[field.Name] = fieldValue ?? field.DefaultValue;
                });
            }

            if (type is ScalarGraphType)
            {
                var scalarType = type as ScalarGraphType;
                return scalarType.Coerce(input);
            }

            return input;
        }

        public Dictionary<string, Fields> CollectFields(ExecutionContext context, GraphType type, Selections selections, Dictionary<string, Fields> fields)
        {
            if (fields == null)
            {
                fields = new Dictionary<string, Fields>();
            }

            selections.Apply(selection =>
            {
                if (selection.Field != null)
                {
                    var name = selection.Field.Alias ?? selection.Field.Name;
                    if (!fields.ContainsKey(name))
                    {
                        fields[name] = new Fields();
                    }
                    fields[name].Add(selection.Field);
                }
                else if (selection.Fragment != null)
                {
                    if (selection.Fragment is FragmentSpread)
                    {
                        var spread = selection.Fragment as FragmentSpread;
                        var fragment = context.Fragments.FindDefinition(spread.Name);
                        if (DoesFragmentConditionMatch(context, fragment, type))
                        {
                            CollectFields(context, type, fragment.Selections, fields);
                        }
                    }
                    else if (selection.Fragment is InlineFragment)
                    {
                        var inline = selection.Fragment as InlineFragment;
                        if (DoesFragmentConditionMatch(context, inline, type))
                        {
                            CollectFields(context, type, inline.Selections, fields);
                        }
                    }
                }
            });

            return fields;
        }

        public bool DoesFragmentConditionMatch(ExecutionContext context, IHaveFragmentType fragment, GraphType type)
        {
            var conditionalType = context.Schema.FindType(fragment.Type);
            if (conditionalType == type)
            {
                return true;
            }

            if (conditionalType is InterfaceGraphType)
            {
                return ((InterfaceGraphType)conditionalType).IsPossibleType(type as IImplementInterfaces);
            }

            return false;
        }
    }
}
