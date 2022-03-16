using System;
using System.Collections.Generic;
using System.Linq;
using GraphQL.Types;
using GraphQL.Validation.Errors;
using GraphQL.Validation.Rules;
using Xunit;

namespace GraphQL.Tests.Validation
{
    public class NoUnusedVariablesFailOnFirstErrorTests : ValidationTestBase<NoUnusedVariables, ValidationSchema>
    {
        [Fact]
        public void multiple_variables_not_used()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
          query Foo($a: String, $b: String, $c: String) {
            field(b: $b)
          }
        ";
                unusedVar(_, "a", "Foo", 2, 21);
                _.FailOnFirstError = true;
            });
        }

        private void unusedVar(
          ValidationTestConfig _,
          string varName,
          string opName,
          int line,
          int column
          )
        {
            _.Error(err =>
            {
                err.Message = NoUnusedVariablesError.UnusedVariableMessage(varName, opName);
                err.Loc(line, column);
            });
        }
    }

    public class ArgumentsOfCorrectType_Invalid_Non_NullableFailOnFirstError : ValidationTestBase<ArgumentsOfCorrectType, ValidationSchema>
    {
        [Fact]
        public void incorrect_value_type()
        {
            var query = @"{
              complicatedArgs {
                multipleReqs(req2: ""two"", req1: ""one"")
              }
            }";

            ShouldFailRule(_ =>
            {
                _.Query = query;
                Rule.badValue(_, "req2", "Int", "\"two\"", 3, 30);
                //Rule.badValue(_, "req1", "Int", "\"one\"", 3, 43);
                _.FailOnFirstError = true;
            });
        }

        // https://github.com/graphql-dotnet/graphql-dotnet/issues/2339
        [Fact]
        public void multiple_args_with_both_null()
        {
            var query = @"{
              complicatedArgs {
                multipleReqs(req2: null, req1: null)
              }
            }";

            ShouldFailRule(_ =>
            {
                _.Query = query;
                Rule.badValue(_, "req2", "Int", "null", 3, 30, "Expected 'Int!', found null.");
                //Rule.badValue(_, "req1", "Int", "null", 3, 42, "Expected 'Int!', found null.");
                _.FailOnFirstError = true;
            });
        }

        [Fact]
        public void with_directives_with_incorrect_types()
        {
            var query = @"{
              dog @include(if: ""yes"") {
                name @skip(if: ENUM)
              }
            }";

            ShouldFailRule(_ =>
            {
                _.Query = query;
                Rule.badValue(_, "if", "Boolean", "\"yes\"", 2, 28);
                //Rule.badValue(_, "if", "Boolean", "ENUM", 3, 28);
                _.FailOnFirstError = true;
            });
        }
    }

    public class DefaultValuesOfCorrectTypeFailOnFirstErrorTests : ValidationTestBase<DefaultValuesOfCorrectType, ValidationSchema>
    {
        [Fact]
        public void variables_with_invalid_default_values()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                    query InvalidDefaultValues(
                        $a: Int = ""one"",
                        $b: String = 4,
                        $c: ComplexInput = ""notverycomplex""
                    ) {
                      dog { name }
                    }";

                _.Error(BadValueForDefaultArgMessage("a", "Int", "\"one\"", "Expected type 'Int', found \"one\"."), 3, 35);
                //_.Error(BadValueForDefaultArgMessage("b", "String", "4", "Expected type 'String', found 4."), 4, 38);
                //_.Error(BadValueForDefaultArgMessage("c", "ComplexInput", "\"notverycomplex\"", "Expected 'ComplexInput', found not an object."), 5, 44);
                //_.Error("Variable '$a' is invalid. Error coercing default value.", 3, 25);
                //_.Error("Variable '$b' is invalid. Error coercing default value.", 4, 25);
                //_.Error("Variable '$c' is invalid. Error coercing default value.", 5, 25);
                _.FailOnFirstError = true;
            });
        }

        [Fact]
        public void list_variables_with_invalid_item()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                    query InvalidItem($a: [String] = [""one"", 2]) {
                      dog { name }
                    }";
                _.Error(BadValueForDefaultArgMessage("a", "[String]", "[\"one\", 2]", "In element #2: [Expected type 'String', found 2.]"), 2, 54);
                //_.Error("Variable '$a' is invalid. Error coercing default value.", 2, 39);
                _.FailOnFirstError = true;
            });
        }

        private static string BadValueForDefaultArgMessage(string varName, string type, string value, string verboseErrors)
            => DefaultValuesOfCorrectTypeError.BadValueForDefaultArgMessage(varName, type, value, verboseErrors);
    }

    public class FieldsOnCorrectTypeFailOnFirstErrorTests : ValidationTestBase<FieldsOnCorrectType, ValidationSchema>
    {
        [Fact]
        public void reports_errors_when_type_is_known_again()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  fragment typeKnownAgain on Pet {
                    unknown_pet_field {
                      ... on Cat {
                        unknown_cat_field
                      }
                    }
                  }
                ";

                undefinedField(_, "unknown_pet_field", "Pet", line: 3, column: 21);
                //undefinedField(_, "unknown_cat_field", "Cat", line: 5, column: 25);
                _.FailOnFirstError = true;
            });
        }

        private void undefinedField(
            ValidationTestConfig _,
            string field,
            string type,
            IEnumerable<string> suggestedTypes = null,
            IEnumerable<string> suggestedFields = null,
            int line = 0,
            int column = 0)
        {
            suggestedTypes ??= Enumerable.Empty<string>();
            suggestedFields ??= Enumerable.Empty<string>();

            _.Error(FieldsOnCorrectTypeError.UndefinedFieldMessage(field, type, suggestedTypes, suggestedFields), line, column);
        }
    }

    public class KnownArgumentNamesFailOnFirstErrorTests : ValidationTestBase<KnownArgumentNames, ValidationSchema>
    {
        [Fact]
        public void invalid_arg_name()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  fragment oneGoodArgOneInvalidArg on Dog {
                    doesKnowCommand(whoknows: 1, dogCommand: SIT, unknown: true)
                  }
                ";
                _.FailOnFirstError= true;
                _.Error(KnownArgumentNamesError.UnknownArgMessage("whoknows", "doesKnowCommand", "Dog", null), 3, 37);
                //_.Error(KnownArgumentNamesError.UnknownArgMessage("unknown", "doesKnowCommand", "Dog", null), 3, 67);
            });
        }

        [Fact]
        public void unknown_args_deeply()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  {
                    dog {
                      doesKnowCommand(unknown: true)
                    }
                    human {
                      pet {
                        ... on Dog {
                          doesKnowCommand(unknown: true)
                        }
                      }
                    }
                  }
                ";
                _.FailOnFirstError = true;
                _.Error(KnownArgumentNamesError.UnknownArgMessage("unknown", "doesKnowCommand", "Dog", null), 4, 39);
                //_.Error(KnownArgumentNamesError.UnknownArgMessage("unknown", "doesKnowCommand", "Dog", null), 9, 43);
            });
        }
    }

    public class KnownDirectivesFailOnFirstErrorTests : ValidationTestBase<KnownDirectivesInAllowedLocations, ValidationSchema>
    {
        private void unknownDirective(ValidationTestConfig _, string name, int line, int column)
        {
            _.Error($"Unknown directive '{name}'.", line, column);
        }

        private void misplacedDirective(ValidationTestConfig _, string name, DirectiveLocation placement, int line, int column)
        {
            _.Error($"Directive '{name}' may not be used on {placement}.", line, column);
        }

        [Fact]
        public void with_many_unknown_directives()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  {
                    dog @unknown(directive: ""value"") {
                      name
                    }
                    human @unknown(directive: ""value"") {
                      name
                      pets @unknown(directive: ""value"") {
                        name
                      }
                    }
                  }
                ";
                _.FailOnFirstError = true;
                unknownDirective(_, "unknown", 3, 25);
                //unknownDirective(_, "unknown", 6, 27);
                //unknownDirective(_, "unknown", 8, 28);
            });
        }

        [Fact]
        public void with_misplaced_directives()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  query Foo @include(if: true) {
                    name @onQuery
                    ...Frag @onQuery
                  }

                  mutation Bar @onQuery {
                    someField
                  }
                ";
                _.FailOnFirstError = true;
                misplacedDirective(_, "include", DirectiveLocation.Query, 2, 29);
                //misplacedDirective(_, "onQuery", DirectiveLocation.Field, 3, 26);
                //misplacedDirective(_, "onQuery", DirectiveLocation.FragmentSpread, 4, 29);
                //misplacedDirective(_, "onQuery", DirectiveLocation.Mutation, 7, 32);
            });
        }
    }

    public class KnownFragmentNamesFailOnFirstErrorTests : ValidationTestBase<KnownFragmentNames, ValidationSchema>
    {
        [Fact]
        public void unknown_fragment_names_are_invalid()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
          {
            human(id: 4) {
              ...UnknownFragment1
              ... on Human {
                ...UnknownFragment2
              }
            }
          }
          fragment HumanFields on Human {
            name
            ...UnknownFragment3
          }
        ";
                _.FailOnFirstError = true;
                undefFrag(_, "UnknownFragment1", 4, 15);
                //undefFrag(_, "UnknownFragment2", 6, 17);
                //undefFrag(_, "UnknownFragment3", 12, 13);
            });
        }

        private void undefFrag(
          ValidationTestConfig _,
          string fragName,
          int line,
          int column)
        {
            _.Error(err =>
            {
                err.Message = KnownFragmentNamesError.UnknownFragmentMessage(fragName);
                err.Loc(line, column);
            });
        }
    }

    public class KnownTypeNamesFailOnFirstErrorTests : ValidationTestBase<KnownTypeNames, ValidationSchema>
    {
        [Fact]
        public void unknown_nonnull_type_name_is_invalid()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                    query Foo($var: Abcd!) {
                        user(id: 4) {
                            pets {
                                ... on Pet { name },
                                ...PetFields
                            }
                        }
                    }
                    fragment PetFields on Pet {
                        name
                    }";
                _.FailOnFirstError = true;
                _.Error(KnownTypeNamesError.UnknownTypeMessage("Abcd", null), 2, 37);
                //_.Error("Variable '$var' is invalid. Variable has unknown type 'Abcd'", 2, 31);
            });
        }

        [Fact]
        public void unknown_type_names_are_invalid()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  query Foo($var: JumbledUpLetters) {
                    user(id: 4) {
                      name
                      pets { ... on Badger { name }, ...PetFields }
                    }
                  }
                  fragment PetFields on Peettt {
                    name
                  }
                ";
                _.FailOnFirstError = true;
                _.Error(KnownTypeNamesError.UnknownTypeMessage("JumbledUpLetters", null), 2, 35);
                //_.Error(KnownTypeNamesError.UnknownTypeMessage("Badger", null), 5, 37);
                //_.Error(KnownTypeNamesError.UnknownTypeMessage("Peettt", new[] { "Pet" }), 8, 41);
                //_.Error("Variable '$var' is invalid. Variable has unknown type 'JumbledUpLetters'", 2, 29);
            });
        }
    }

    public class LoneAnonymousOperationFailOnFirstErrorTests : ValidationTestBase<LoneAnonymousOperation, ValidationSchema>
    {
        [Fact]
        public void multiple_anon_operations()
        {
            var query = @"
                {
                  fieldA
                }

                {
                  fieldB
                }
                ";

            ShouldFailRule(_ =>
            {
                _.Query = query;
                _.FailOnFirstError = true;
                _.Error(LoneAnonymousOperationError.AnonOperationNotAloneMessage(), 2, 17);
                //_.Error(LoneAnonymousOperationError.AnonOperationNotAloneMessage(), 6, 17);
            });
        }
    }

    public class NoFragmentCyclesFailOnFirstErrorTests : ValidationTestBase<NoFragmentCycles, ValidationSchema>
    {
        [Fact]
        public void no_spreading_itself_deeply()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  fragment fragA on Dog { ...fragB }
                  fragment fragB on Dog { ...fragC }
                  fragment fragC on Dog { ...fragO }
                  fragment fragX on Dog { ...fragY }
                  fragment fragY on Dog { ...fragZ }
                  fragment fragZ on Dog { ...fragO }
                  fragment fragO on Dog { ...fragP }
                  fragment fragP on Dog { ...fragA, ...fragX }
                ";
                _.FailOnFirstError = true;
                _.Error(e =>
                {
                    e.Message = CycleErrorMessage("fragA", new[] { "fragB", "fragC", "fragO", "fragP" });
                    e.Loc(2, 43);
                    e.Loc(3, 43);
                    e.Loc(4, 43);
                    e.Loc(8, 43);
                    e.Loc(9, 43);
                });
                //_.Error(e =>
                //{
                //    e.Message = CycleErrorMessage("fragO", new[] { "fragP", "fragX", "fragY", "fragZ" });
                //    e.Loc(8, 43);
                //    e.Loc(9, 53);
                //    e.Loc(5, 43);
                //    e.Loc(6, 43);
                //    e.Loc(7, 43);
                //});
            });
        }

        [Fact]
        public void no_spreading_itself_deeply_two_paths()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  fragment fragA on Dog { ...fragB, ...fragC }
                  fragment fragB on Dog { ...fragA }
                  fragment fragC on Dog { ...fragA }
                ";
                _.FailOnFirstError = true;
                _.Error(e =>
                {
                    e.Message = CycleErrorMessage("fragA", new[] { "fragB" });
                    e.Loc(2, 43);
                    e.Loc(3, 43);
                });
                //_.Error(e =>
                //{
                //    e.Message = CycleErrorMessage("fragA", new[] { "fragC" });
                //    e.Loc(2, 53);
                //    e.Loc(4, 43);
                //});
            });
        }

        [Fact]
        public void no_spreading_itself_deeply_two_paths_alt_traverse_order()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  fragment fragA on Dog { ...fragC }
                  fragment fragB on Dog { ...fragC }
                  fragment fragC on Dog { ...fragA, ...fragB }
                ";
                _.FailOnFirstError = true;
                _.Error(e =>
                {
                    e.Message = CycleErrorMessage("fragA", new[] { "fragC" });
                    e.Loc(2, 43);
                    e.Loc(4, 43);
                });
                //_.Error(e =>
                //{
                //    e.Message = CycleErrorMessage("fragC", new[] { "fragB" });
                //    e.Loc(4, 53);
                //    e.Loc(3, 43);
                //});
            });
        }

        [Fact]
        public void no_spreading_itself_deeply_and_immediately()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  fragment fragA on Dog { ...fragB }
                  fragment fragB on Dog { ...fragB, ...fragC }
                  fragment fragC on Dog { ...fragA, ...fragB }
                ";
                _.FailOnFirstError = true;
                _.Error(e =>
                {
                    e.Message = CycleErrorMessage("fragB", Array.Empty<string>());
                    e.Loc(3, 43);
                });
                //_.Error(e =>
                //{
                //    e.Message = CycleErrorMessage("fragA", new[] { "fragB", "fragC" });
                //    e.Loc(2, 43);
                //    e.Loc(3, 53);
                //    e.Loc(4, 43);
                //});
                //_.Error(e =>
                //{
                //    e.Message = CycleErrorMessage("fragB", new[] { "fragC" });
                //    e.Loc(3, 53);
                //    e.Loc(4, 53);
                //});
            });
        }

        private static string CycleErrorMessage(string fragName, string[] spreadNames)
            => NoFragmentCyclesError.CycleErrorMessage(fragName, spreadNames);
    }

    public class NoUndefinedVariablesFailOnFirstErrorTests : ValidationTestBase<NoUndefinedVariables, ValidationSchema>
    {
        [Fact]
        public void multiple_variables_not_defined()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  query Foo($b: String) {
                    field(a: $a, b: $b, c: $c)
                  }
                ";
                _.FailOnFirstError = true;
                undefVar(_, "a", 3, 30, "Foo", 2, 19);
                //undefVar(_, "c", 3, 44, "Foo", 2, 19);
            });
        }

        [Fact]
        public void multiple_variables_in_fragments_not_defined()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  query Foo($b: String) {
                    ...FragA
                  }
                  fragment FragA on Type {
                    field(a: $a) {
                      ...FragB
                    }
                  }
                  fragment FragB on Type {
                    field(b: $b) {
                      ...FragC
                    }
                  }
                  fragment FragC on Type {
                    field(c: $c)
                  }
                ";
                _.FailOnFirstError = true;
                undefVar(_, "a", 6, 30, "Foo", 2, 19);
                //undefVar(_, "c", 16, 30, "Foo", 2, 19);
            });
        }

        [Fact]
        public void single_variable_in_fragment_not_defined_by_multiple_operations()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  query Foo($a: String) {
                    ...FragAB
                  }
                  query Bar($a: String) {
                    ...FragAB
                  }
                  fragment FragAB on Type {
                    field(a: $a, b: $b)
                  }
                ";
                _.FailOnFirstError = true;
                undefVar(_, "b", 9, 37, "Foo", 2, 19);
                //undefVar(_, "b", 9, 37, "Bar", 5, 19);
            });
        }

        [Fact]
        public void variables_in_fragment_not_defined_by_multiple_operations()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  query Foo($b: String) {
                    ...FragAB
                  }
                  query Bar($a: String) {
                    ...FragAB
                  }
                  fragment FragAB on Type {
                    field(a: $a, b: $b)
                  }
                ";
                _.FailOnFirstError = true;
                undefVar(_, "a", 9, 30, "Foo", 2, 19);
                //undefVar(_, "b", 9, 37, "Bar", 5, 19);
            });
        }

        [Fact]
        public void variable_in_fragment_used_by_other_operation()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  query Foo($b: String) {
                    ...FragA
                  }
                  query Bar($a: String) {
                    ...FragB
                  }
                  fragment FragA on Type {
                    field(a: $a)
                  }
                  fragment FragB on Type {
                    field(b: $b)
                  }
                ";
                _.FailOnFirstError = true;
                undefVar(_, "a", 9, 30, "Foo", 2, 19);
                //undefVar(_, "b", 12, 30, "Bar", 5, 19);
            });
        }

        [Fact]
        public void multiple_undefined_variables_produce_multiple_errors()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  query Foo($b: String) {
                    ...FragAB
                  }
                  query Bar($a: String) {
                    ...FragAB
                  }
                  fragment FragAB on Type {
                    field1(a: $a, b: $b)
                    ...FragC
                    field3(a: $a, b: $b)
                  }
                  fragment FragC on Type {
                    field2(c: $c)
                  }
                ";
                _.FailOnFirstError = true;
                undefVar(_, "a", 9, 31, "Foo", 2, 19);
                //undefVar(_, "a", 11, 31, "Foo", 2, 19);
                //undefVar(_, "c", 14, 31, "Foo", 2, 19);
                //undefVar(_, "b", 9, 38, "Bar", 5, 19);
                //undefVar(_, "b", 11, 38, "Bar", 5, 19);
                //undefVar(_, "c", 14, 31, "Bar", 5, 19);
            });
        }

        private void undefVar(
            ValidationTestConfig _,
            string varName,
            int line1,
            int column1,
            string opName,
            int line2,
            int column2)
        {
            _.Error(err =>
            {
                err.Message = NoUndefinedVariablesError.UndefinedVarMessage(varName, opName);
                err.Loc(line1, column1);
                err.Loc(line2, column2);
            });
        }
    }

    public class NoUnusedFragmentsFailOnFirstErrorTests : ValidationTestBase<NoUnusedFragments, ValidationSchema>
    {
        [Fact]
        public void contains_unknown_fragments()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
          query Foo {
            human(id: 4) {
              ...HumanFields1
            }
          }
          query Bar {
            human(id: 4) {
              ...HumanFields2
            }
          }
          fragment HumanFields1 on Human {
            name
            ...HumanFields3
          }
          fragment HumanFields2 on Human {
            name
          }
          fragment HumanFields3 on Human {
            name
          }
          fragment Unused1 on Human {
            name
          }
          fragment Unused2 on Human {
            name
          }
        ";
                _.FailOnFirstError = true;
                unusedFrag(_, "Unused1", 22, 11);
                //unusedFrag(_, "Unused2", 25, 11);
            });
        }

        [Fact]
        public void contains_unknown_fragments_with_ref_cycle()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
          query Foo {
            human(id: 4) {
              ...HumanFields1
            }
          }
          query Bar {
            human(id: 4) {
              ...HumanFields2
            }
          }
          fragment HumanFields1 on Human {
            name
            ...HumanFields3
          }
          fragment HumanFields2 on Human {
            name
          }
          fragment HumanFields3 on Human {
            name
          }
          fragment Unused1 on Human {
            name
            ...Unused2
          }
          fragment Unused2 on Human {
            name
            ...Unused1
          }
        ";
                _.FailOnFirstError = true;
                unusedFrag(_, "Unused1", 22, 11);
                //unusedFrag(_, "Unused2", 26, 11);
            });
        }

        private void unusedFrag(
          ValidationTestConfig _,
          string varName,
          int line,
          int column
          )
        {
            _.Error(err =>
            {
                err.Message = NoUnusedFragmentsError.UnusedFragMessage(varName);
                err.Loc(line, column);
            });
        }
    }

    public class OverlappingFieldsCanBeMergedFailOnFirstErrorTest : ValidationTestBase<OverlappingFieldsCanBeMerged, ValidationSchema>
    {
        [Fact]
        public void Reports_each_conflict_once_should_fail()
        {
            const string query = @"
                {
                    f1 {
                        ...A
                        ...B
                    }
                    f2 {
                        ...B
                        ...A
                    }
                    f3 {
                        ...A
                        ...B
                        x: c
                    }
                }
                fragment A on Type {
                    x: a
                }
                fragment B on Type {
                    x: b
                }
            ";

            ShouldFailRule(config =>
            {
                config.Query = query;
                config.Error(e =>
                {
                    e.Message = OverlappingFieldsCanBeMergedError.FieldsConflictMessage("x", new OverlappingFieldsCanBeMerged.ConflictReason
                    {
                        Message = new OverlappingFieldsCanBeMerged.Message
                        {
                            Msg = "a and b are different fields"
                        }
                    });
                    e.Locations.Add(new ErrorLocation(18, 21));
                    e.Locations.Add(new ErrorLocation(21, 21));
                });
                config.FailOnFirstError = true;
                //config.Error(e =>
                //{
                //    e.Message = OverlappingFieldsCanBeMergedError.FieldsConflictMessage("x", new OverlappingFieldsCanBeMerged.ConflictReason
                //    {
                //        Message = new OverlappingFieldsCanBeMerged.Message
                //        {
                //            Msg = "c and a are different fields"
                //        }
                //    });
                //    e.Locations.Add(new ErrorLocation(14, 25));
                //    e.Locations.Add(new ErrorLocation(18, 21));
                //});

                //config.Error(e =>
                //{
                //    e.Message = OverlappingFieldsCanBeMergedError.FieldsConflictMessage("x", new OverlappingFieldsCanBeMerged.ConflictReason
                //    {
                //        Message = new OverlappingFieldsCanBeMerged.Message
                //        {
                //            Msg = "c and b are different fields"
                //        }
                //    });
                //    e.Locations.Add(new ErrorLocation(14, 25));
                //    e.Locations.Add(new ErrorLocation(21, 21));
                //});
            });
        }
    }

    public class ProvidedNonNullArgumentsFailOnFirstErrorTests : ValidationTestBase<ProvidedNonNullArguments, ValidationSchema>
    {
        [Fact]
        public void missing_multiple_non_null_argument()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"{
                  complicatedArgs {
                    multipleReqs
                  }
                }";
                _.FailOnFirstError = true;
                missingFieldArg(_, "multipleReqs", "req1", "Int!", 3, 21);
                //missingFieldArg(_, "multipleReqs", "req2", "Int!", 3, 21);
            });
        }

        [Fact]
        public void directive_with_missing_types()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                {
                  dog @include {
                    name @skip
                  }
                }";
                _.FailOnFirstError = true;
                missingDirectiveArg(_, "include", "if", "Boolean!", 3, 23);
                //missingDirectiveArg(_, "skip", "if", "Boolean!", 4, 26);
            });
        }

        private void missingFieldArg(
            ValidationTestConfig _,
            string fieldName,
            string argName,
            string typeName,
            int line,
            int column)
        {
            _.Error(ProvidedNonNullArgumentsError.MissingFieldArgMessage(fieldName, argName, typeName), line, column);
        }

        private void missingDirectiveArg(
            ValidationTestConfig _,
            string directiveName,
            string argName,
            string typeName,
            int line,
            int column)
        {
            _.Error(ProvidedNonNullArgumentsError.MissingDirectiveArgMessage(directiveName, argName, typeName), line, column);
        }
    }

    public class UniqueArgumentNamesFailOnFirstErrorTests : ValidationTestBase<UniqueArgumentNames, ValidationSchema>
    {
        [Fact]
        public void many_duplicate_field_arguments()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
          {
            field(arg1: ""value"", arg1: ""value"", arg1: ""value"")
          }
        ";
                _.FailOnFirstError = true;
                duplicateArg(_, "arg1", 3, 19, 3, 34);
                //duplicateArg(_, "arg1", 3, 19, 3, 49);
            });
        }

        [Fact]
        public void many_duplicate_directive_arguments()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
          {
            field @directive(arg1: ""value"", arg1: ""value"", arg1: ""value"")
          }
        ";
                _.FailOnFirstError = true;
                duplicateArg(_, "arg1", 3, 30, 3, 45);
                //duplicateArg(_, "arg1", 3, 30, 3, 60);
            });
        }

        private void duplicateArg(
          ValidationTestConfig _,
          string argName,
          int line1,
          int column1,
          int line2,
          int column2)
        {
            _.Error(err =>
            {
                err.Message = UniqueArgumentNamesError.DuplicateArgMessage(argName);
                err.Loc(line1, column1);
                err.Loc(line2, column2);
            });
        }
    }

    public class UniqueDirectivesPerLocationFailOnFirstErrorTests : ValidationTestBase<UniqueDirectivesPerLocation, ValidationSchema>
    {
        [Fact]
        public void duplicate_directives_in_one_location()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                fragment Test on Type {
                    field @directive @directive
                }
                ";
                _.FailOnFirstError = true;
                duplicateDirective(_, "directive", 3, 27);
                //duplicateDirective(_, "directive", 3, 38);
            });
        }

        [Fact]
        public void many_duplicate_directives_in_one_location()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                fragment Test on Type {
                    field @directive @directive @directive
                }
                ";
                _.FailOnFirstError = true;
                duplicateDirective(_, "directive", 3, 27);
                //duplicateDirective(_, "directive", 3, 38);
                //duplicateDirective(_, "directive", 3, 49);
            });
        }

        [Fact]
        public void different_duplicate_directives_in_one_location()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                fragment Test on Type {
                    field @directiveA @directiveB @directiveA @directiveB
                }
                ";
                _.FailOnFirstError = true;
                duplicateDirective(_, "directiveA", 3, 27);
                //duplicateDirective(_, "directiveB", 3, 39);
                //duplicateDirective(_, "directiveA", 3, 51);
                //duplicateDirective(_, "directiveB", 3, 63);
            });
        }

        [Fact]
        public void duplicate_directives_in_many_locations()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                fragment Test on Type @directive @directive {
                    field @directive @directive
                }
                ";
                _.FailOnFirstError = true;
                duplicateDirective(_, "directive", 2, 39);
                //duplicateDirective(_, "directive", 2, 50);
                //duplicateDirective(_, "directive", 3, 27);
                //duplicateDirective(_, "directive", 3, 38);
            });
        }

        private void duplicateDirective(ValidationTestConfig _, string directiveName, int line, int column)
        {
            _.Error(err =>
            {
                err.Message = $"The directive '{directiveName}' can only be used once at this location.";
                err.Loc(line, column);
            });
        }
    }

    public class UniqueInputFieldNamesFailOnFirstErrorTests : ValidationTestBase<UniqueInputFieldNames, ValidationSchema>
    {
        [Fact]
        public void many_duplicate_input_object_fields()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  {
                    field(arg: { f1: ""value"", f1: ""value"", f1: ""value"" })
                  }
                ";
                _.FailOnFirstError = true;
                _.Error(x =>
                {
                    x.Message = UniqueInputFieldNamesError.DuplicateInputField("f1");
                    x.Loc(3, 38);
                    x.Loc(3, 51);
                });
                //_.Error(x =>
                //{
                //    x.Message = UniqueInputFieldNamesError.DuplicateInputField("f1");
                //    x.Loc(3, 38);
                //    x.Loc(3, 64);
                //});
            });
        }
    }

    public class UniqueVariableNamesFailOnFirstErrorTests : ValidationTestBase<UniqueVariableNames, ValidationSchema>
    {
        [Fact]
        public void duplicate_variable_names()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
          query A($x: Int, $x: Int, $x: String) { __typename }
          query B($x: String, $x: Int) { __typename }
          query C($x: Int, $x: Int) { __typename }
        ";
                _.FailOnFirstError = true;
                duplicateVariable(_, "x", 2, 19, 2, 28);
                //duplicateVariable(_, "x", 2, 19, 2, 37);
                //duplicateVariable(_, "x", 3, 19, 3, 31);
                //duplicateVariable(_, "x", 4, 19, 4, 28);
            });
        }

        private void duplicateVariable(
          ValidationTestConfig _,
          string variableName,
          int line1,
          int column1,
          int line2,
          int column2)
        {
            _.Error(err =>
            {
                err.Message = UniqueVariableNamesError.DuplicateVariableMessage(variableName);
                err.Loc(line1, column1);
                err.Loc(line2, column2);
            });
        }
    }

    public class VariablesAreInputTypesFailOnFirstErrorTests : ValidationTestBase<VariablesAreInputTypes, ValidationSchema>
    {
        [Fact]
        public void output_types_are_invalid()
        {
            ShouldFailRule(_ =>
            {
                _.Query = @"
                  query Foo($a: Dog, $b: [[CatOrDog!]]!, $c: Pet) {
                    field(a: $a, b: $b, c: $c)
                  }
                ";
                _.FailOnFirstError = true;
                _.Error(
                    message: VariablesAreInputTypesError.UndefinedVarMessage("a", "Dog"),
                    line: 2,
                    column: 29);
                //_.Error(
                //    message: VariablesAreInputTypesError.UndefinedVarMessage("b", "[[CatOrDog!]]!"),
                //    line: 2,
                //    column: 38);
                //_.Error(
                //    message: VariablesAreInputTypesError.UndefinedVarMessage("c", "Pet"),
                //    line: 2,
                //    column: 58);
                //_.Error(
                //   message: "Variable '$b' is invalid. No value provided for a non-null variable.",
                //   line: 2,
                //   column: 38);
            });
        }
    }
}
