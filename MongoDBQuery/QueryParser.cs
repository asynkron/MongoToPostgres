using System.Globalization;
using System.Text;
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

    private static string GetPredicate(string path, KeyValuePair<string, JToken?> prop, JToken firstProp, string op)
    {
        var val = GetPrimitive(firstProp);
        var type = GetTypeHint(firstProp);
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

    private static string GetInPredicate(string path, JProperty firstProp, KeyValuePair<string, JToken?> prop)
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
        var lines = new List<string>();
        foreach (var prop in parentObject)
        {
            if (prop.Key.StartsWith("$"))
            {
                if (prop.Key == "$or")
                {
                    return GetOrPredicate(path, prop);
                }

                return "UNKNOWN";
            }

            //normal object, AND predicates together
            if (prop.Value is JObject childObject)
            {
                var childProps = childObject.Children().ToArray();
                if (childProps.FirstOrDefault() is JProperty firstProp && firstProp.Name.StartsWith("$"))
                {
                    var cond = firstProp.Name switch
                    {
                        "$in"  => GetInPredicate(path, firstProp, prop),
                        "$gte" => GetPredicate(path, prop, firstProp.Value, ">="),
                        "$gt"  => GetPredicate(path, prop, firstProp.Value, ">"),
                        "$lt"   => GetPredicate(path, prop, firstProp.Value, "<"),
                        "$lte"  => GetPredicate(path, prop, firstProp.Value, "<="),
                        _      => ""
                    };
                    lines.Add(cond);
                }
                else
                {
                    BuildSql(childObject, $"{path} '{prop.Key}'->");
                }
            }
            else
            {
                lines.Add(GetPredicate(path, prop, prop.Value!, "="));
            }
        }

        return string.Join(" AND ", lines);
        ;
    }

    private static string GetOrPredicate(string path, KeyValuePair<string, JToken?> prop)
    {
        var parts = (JArray)prop.Value!;
        var x = parts.Cast<JObject>().ToArray();
        var res = x.Select(y => BuildSql(y, path)).ToArray();
        return $"( {string.Join(" OR ", res)} )";
    }

    private static string GetKey(string path, KeyValuePair<string, JToken?> prop)
    {
        var keys = prop.Key.Split(".").Select(k => $"'{k}'").ToArray();
        var key = string.Join("->", keys.Take(keys.Length-1));
        if (key != "")
        {
            key = "->" + key;
        }
        var last = keys.Last();
        return $"{path}{key}->>{last}";
    }
}