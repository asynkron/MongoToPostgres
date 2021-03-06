using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MongoDBQuery;

public static class QueryParser
{
    public static string ToSql(string json, string jsonField)
    {
        if (json == null) throw new ArgumentNullException(nameof(json));
        var parentObject = JsonConvert.DeserializeObject<JObject>(json)!;
        return $"WHERE {GetLogicalPredicate(parentObject, $"{jsonField}")}";
    }

    private static string GetPredicate(string path, JProperty prop, JToken operationProp, string op)
    {
        var val = GetPrimitive(operationProp);
        var key = GetKey(path, prop);
        var cond = $"(({key}) {op} {val})";
        return cond;
    }

    private static string GetPrimitive(JToken prop)
    {
        var raw = GetPrimitiveInner(prop);
        var escaped = raw.Replace("'", "''");
        return $"'{escaped}'::jsonb";
    }

    private static string GetPrimitiveInner(JToken prop) =>
        prop.Type switch
        {
            JTokenType.Integer => $"{prop}",
            JTokenType.Float   => $"{prop}",
            JTokenType.String  => $"\"{prop}\"",
            JTokenType.Boolean => $"{prop}",
            JTokenType.Array   => $"{prop}",
            JTokenType.Null    => "null",
            _                  => throw new ArgumentOutOfRangeException()
        };

    private static string GetInPredicate(string path, JProperty firstProp, JProperty prop)
    {
        var x = (JArray)firstProp.Value;
        var values = x.Cast<JValue>();
        var values2 = values.Select(GetPrimitive);
        var key = GetKey(path, prop);

        var val = string.Join(", ", values2);
        var cond = $"(({key}) IN ({val}))";

        return cond;
    }

    private static string GetLogicalPredicate(JObject parentObject, string path)
    {
        //OR
        foreach (var prop in parentObject.Properties().Where(p => p.Name.StartsWith("$")))
        {
            if (prop.Name == "$or") return GetOrPredicate(path, prop);
            return "UNKNOWN";
        }

        //AND
        return GetAndPredicate(parentObject, path);
    }

    private static string GetAndPredicate(JObject parentObject, string path)
    {
        var lines = new List<string>();
        //normal object, AND predicates together
        foreach (var prop in parentObject.Properties())
        {
            if (prop.Value is JObject childObject)
            {
                var childProps = childObject.Properties().Where(p => p.Name.StartsWith("$")).ToArray();
                if (childProps.Any())
                {
                    foreach (var firstProp in childProps)
                    {
                        var cond = GetAnyPredicate(path, firstProp, prop);
                        if (cond != null) lines.Add(cond);
                    }
                }
                else
                {
                    GetLogicalPredicate(childObject, $"{path} '{prop.Name}'->");
                }
            }
            else
            {
                var predicate = GetPredicate(path, prop, prop.Value, "<@");
                lines.Add(predicate);
            }
        }

        return string.Join("\nAND ", lines);
    }

    private static string? GetAnyPredicate(string path, JProperty firstProp, JProperty prop) =>
        firstProp.Name switch
        {
            "$in"        => GetInPredicate(path, firstProp, prop),
            "$not"       => GetNotPredicate(path, prop, firstProp),
            "$eq"        => GetPredicate(path, prop, firstProp.Value, "="),
            "$neq"       => GetPredicate(path, prop, firstProp.Value, "!="),
            "$gte"       => GetPredicate(path, prop, firstProp.Value, ">="),
            "$gt"        => GetPredicate(path, prop, firstProp.Value, ">"),
            "$lt"        => GetPredicate(path, prop, firstProp.Value, "<"),
            "$lte"       => GetPredicate(path, prop, firstProp.Value, "<="),
            "$regex"     => GetPredicate(path, prop, firstProp.Value, "~"),
            "$elemMatch" => GetElemMatchPredicate(path, prop, firstProp.Value as JObject),
            "$all"       => GetAllMatchPredicate(path, prop, firstProp.Value),
            _            => null
        };

    private static string GetAllMatchPredicate(string path, JProperty prop, JToken firstPropValue)
    {
        throw new NotImplementedException();
    }

    private static string GetElemMatchPredicate(string path, JProperty prop, JObject? predicateObject)
    {
        if (predicateObject == null) throw new ArgumentNullException(nameof(predicateObject));
        
        var key = GetKey(path, prop);
        var pred = GetLogicalPredicate(predicateObject, "data");

        var cond = 
            $@"EXISTS (SELECT true 
            FROM jsonb_array_elements({key}) AS t(data)
            WHERE
            {pred}
        )";

        return cond;
    }

    private static string GetNotPredicate(string path, JProperty prop, JProperty firstProp)
    {
        var o = (JObject)firstProp.Value;
        var p = o.Properties().First();
        var x = GetAnyPredicate(path, p, prop);
        return $"(NOT {x})";
    }

    private static string GetOrPredicate(string path, JProperty prop)
    {
        var parts = (JArray)prop.Value;
        var x = parts.Cast<JObject>().ToArray();
        var res = x.Select(y => GetLogicalPredicate(y, path)).ToArray();
        return $"( {string.Join(" OR ", res)} )";
    }

    private static string GetKey(string path, JProperty prop)
    {
        var keys = prop.Name.Split(".").Select(k => $"'{k}'").ToArray();
        var key = string.Join("->", keys);
        if (key != "") key = "->" + key;
        return $"{path}{key}";
    }
}