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
}
