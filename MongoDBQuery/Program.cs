using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

Console.WriteLine("Hello, World!");

var json = @"{$or:[{'age.year': {$gte: 21}, name: 'julio', contribs: { $in: [ 'ALGOL', 'Lisp' ]}}, {x:123}]}";

var sql = BuildSqlOuter(json);
Console.WriteLine(sql);

//TODO:
// * Dot-notation keys
// * Array comparison (subqueries?)
// * In vs Contains what is what here?

static string BuildSqlOuter(string json)
{
    if (json == null) throw new ArgumentNullException(nameof(json));
    
    var parentObject = JsonConvert.DeserializeObject<JObject>(json)!;
    var sb = new StringBuilder();
    sb.AppendLine("where");

    return BuildSql(parentObject, "json ->");
}

//todo: escape this

static string GetPredicate(string path, KeyValuePair<string, JToken?> prop,JToken firstProp, string op)
{
    var val = GetPrimitive(firstProp);
    var key = GetKey(path, prop);
    var cond = $"({key} {op} {val})";
    return cond;
}

static string GetPrimitive(JToken prop) =>
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

static string EscapeString(string sqlStr) => sqlStr;

static string GetInPredicate(string path, JProperty firstProp, KeyValuePair<string, JToken?> prop)
{
    var x = firstProp.Value as JArray;
    var values = x.Cast<JValue>();
    var values2 = values.Select(GetPrimitive);
    var key = GetKey(path, prop);
    var cond = $"({key} IN ({string.Join(", ", values2)}))";
    return cond;
}

static string BuildSql(JObject parentObject, string path)
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
                    "lt"   => GetPredicate(path, prop, firstProp.Value, "<"),
                    "lte"  => GetPredicate(path, prop, firstProp.Value, "<="),
                    _      => ""
                };
                lines.Add(cond);
            }
            else
            {
                BuildSql(childObject, $"{path} '{prop.Key}' ->> ");
            }
        }
        else
        {
            lines.Add(GetPredicate(path, prop,prop.Value!, "="));
        }
    }

    return string.Join(" AND ", lines);;
}

static string GetOrPredicate(string path, KeyValuePair<string, JToken?> prop)
{
    var parts = (JArray)prop.Value!;
    var x = parts.Cast<JObject>().ToArray();
    var res = x.Select(y => BuildSql(y, path)).ToArray();
    return $"( {string.Join(" OR ", res)} )";
}

static string GetKey(string path, KeyValuePair<string, JToken?> prop)
{
    var keys = prop.Key.Split(".").Select(k => $"'{k}'");
    var key = string.Join(" ->> ", keys);
    return $"{path} {key}";
}