using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MongoDBQuery;

public static class QueryParser
{
    //TODO:
    // * Array comparison (sub-queries?)
    // * In vs Contains what is what here?

    public static string ToSql(string json, string jsonField)
    {
        if (json == null) throw new ArgumentNullException(nameof(json));

        var parentObject = JsonConvert.DeserializeObject<JObject>(json)!;

        return "WHERE" + "\n" + BuildSql(parentObject, $"{jsonField}");
    }

    private static string GetPredicate(string path, JProperty prop, JToken operationProp, string op)
    {
        var val = GetPrimitive(operationProp);
        var type = GetTypeHint(operationProp);
        var key = GetKey(path, prop);
        var cond = $"(({key}){type} {op} {val})";
        return cond;
    }

    private static string GetPrimitive(JToken prop) =>
        prop.Type switch
        {
            JTokenType.Integer => prop.Value<int>().ToString(CultureInfo.InvariantCulture),
            JTokenType.Float   => prop.Value<float>().ToString(CultureInfo.InvariantCulture),
            JTokenType.String  => $"'{EscapeString(prop.Value<string>()!)}'",
            JTokenType.Boolean => prop.Value<bool>().ToString(CultureInfo.InvariantCulture),
            JTokenType.Array   => "ARRAY",
            JTokenType.Null    => "null",
            _                  => throw new ArgumentOutOfRangeException()
        };
    
    private static string GetTypeHint(JToken prop) =>
        prop.Type switch
        {
            JTokenType.Integer => "::float",
            JTokenType.Float   => "::float",
            JTokenType.String  => "::text",
            JTokenType.Boolean => "::bool",
            _                  => "",
        };

    //TODO: make proper escape
    private static string EscapeString(string sqlStr) => sqlStr.Replace("\\", "\\\\");

    private static string GetInPredicate(string path, JProperty firstProp, JProperty prop)
    {
        var x = firstProp.Value as JArray;
        var values = x.Cast<JValue>();
        var values2 = values.Select(GetPrimitive);
        var key = GetKey(path, prop);
        var cond = $"({key} IN ({string.Join(", ", values2)}))";
        return cond;
    }

    private static string BuildSql(JObject parentObject, string path)
    {
        foreach (var prop in parentObject.Properties().Where(p => p.Name.StartsWith("$")))
        {
            if (prop.Name == "$or")
            {
                return GetOrPredicate(path, prop);
            }

            return "UNKNOWN";
        }

        return GetAndPredicate(parentObject, path);
    }

    private static string GetAndPredicate(JObject parentObject, string path)
    {
        var lines = new List<string>();
        foreach (var prop in parentObject.Properties())
        {
            //normal object, AND predicates together
            if (prop.Value is JObject childObject)
            {
                var childProps = childObject.Properties().Where(p => p.Name.StartsWith("$")).ToArray();
                if (childProps.Any())
                {
                    foreach (var firstProp in childProps)
                    {
                        var cond = GetAnyPredicate(path, firstProp, prop);
                        if (cond != null)
                        {
                            lines.Add(cond);
                        }
                    }
                }
                else
                {
                    BuildSql(childObject, $"{path} '{prop.Name}'->");
                }
            }
            else
            {
                var predicate = GetPredicate(path, prop, prop.Value, "=");
                lines.Add(predicate);
            }
        }

        return string.Join(" AND ", lines);
    }

    private static string? GetAnyPredicate(string path, JProperty firstProp, JProperty prop) =>
        firstProp.Name switch
        {
            "$in"    => GetInPredicate(path, firstProp, prop),
            "$gte"   => GetPredicate(path, prop, firstProp.Value, ">="),
            "$gt"    => GetPredicate(path, prop, firstProp.Value, ">"),
            "$lt"    => GetPredicate(path, prop, firstProp.Value, "<"),
            "$lte"   => GetPredicate(path, prop, firstProp.Value, "<="),
            "$regex" => GetPredicate(path, prop, firstProp.Value, "~"),
            _        => null
        };

    private static string GetOrPredicate(string path, JProperty prop)
    {
        var parts = (JArray)prop.Value;
        var x = parts.Cast<JObject>().ToArray();
        var res = x.Select(y => BuildSql(y, path)).ToArray();
        return $"( {string.Join(" OR ", res)} )";
    }

    private static string GetKey(string path, JProperty prop)
    {
        var keys = prop.Name.Split(".").Select(k => $"'{k}'").ToArray();
        var key = string.Join("->", keys.Take(keys.Length-1));
        if (key != "")
        {
            key = "->" + key;
        }
        var last = keys.Last();
        return $"{path}{key}->>{last}";
    }
}