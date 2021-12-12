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
        return $"WHERE {GetLogicalPredicate(parentObject, $"{jsonField}")}";
    }

    private static string GetPredicate(string path, JProperty prop, JToken operationProp, string op)
    {
        var val = GetPrimitive(operationProp);
        var key = GetKey(path, prop);
        var cond = $"(({key}) {op} {val})";
        return cond;
    }

    private static string GetPrimitive(JToken prop) =>
        prop.Type switch
        {
            JTokenType.Integer => $"'{prop.Value<int>().ToString(CultureInfo.InvariantCulture)}'::jsonb",
            JTokenType.Float   => $"'{prop.Value<float>().ToString(CultureInfo.InvariantCulture)}'::jsonb",
            JTokenType.String  => $"'\"{EscapeString(prop.Value<string>()!)}\"'"+ "::jsonb",
            JTokenType.Boolean =>$"'{prop.Value<bool>().ToString(CultureInfo.InvariantCulture)}'::jsonb",
            JTokenType.Array   => GetArray(prop),
            JTokenType.Null    => "null",
            _                  => throw new ArgumentOutOfRangeException()
        };

    private static string GetArray(JToken array)
    {
        var x = array as JArray;
        var values = x.Cast<JValue>();
        var values2 = values.Select(GetPrimitive);
        var elements = string.Join(", ", values2);
        var cond = $"[{elements}]";
        return cond;
    }

    //TODO: make proper escape
    private static string EscapeString(string sqlStr) => sqlStr.Replace("\\", "\\\\");

    private static string GetInPredicate(string path, JProperty firstProp, JProperty prop)
    {
        //$in should operate on scalar values.. TODO rewrite

        var x = firstProp.Value as JArray;
        var values = x.Cast<JValue>();
        var values2 = values.Select(GetPrimitive);
        var key = GetKey(path, prop);

        var val = string.Join(", ", values2) ;
        var cond = $"(({key}) IN ({val}))";
    //     var cond = $@"(
    // EXISTS(
    //     SELECT t123.e123 
    //     FROM jsonb_array_elements(({key})::jsonb) AS t123(e123) 
    //     WHERE e123::int IN ()
    // ))";
    
    //     var cond = $@"(
    // EXISTS(
    //     SELECT t123.e123 
    //     FROM jsonb_array_elements(({key})::jsonb) AS t123(e123) 
    //     WHERE e123::int IN ({string.Join(", ", values2)})
    // ))";
        
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
                var predicate = GetPredicate(path, prop, prop.Value, "=");
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
            "$elemMatch" => GetElemMatchPredicate(path, prop, firstProp.Value),
            "$all"       => GetAllMatchPredicate(path, prop, firstProp.Value),
            _            => null
        };

    private static string? GetAllMatchPredicate(string path, JProperty prop, JToken firstPropValue)
    {
        throw new NotImplementedException();
    }

    private static string? GetElemMatchPredicate(string path, JProperty prop, JToken firstPropValue)
    {
        throw new NotImplementedException();
    }

    private static string? GetNotPredicate(string path, JProperty prop, JProperty firstProp)
    {
        var o = firstProp.Value as JObject;
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