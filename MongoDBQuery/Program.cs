using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

Console.WriteLine("Hello, World!");

var json = @"{age: {$gte: 21}, name: 'julio', contribs: { $in: [ 'ALGOL', 'Lisp' ]}}";

var sql = BuildSql(json);
Console.WriteLine(sql);


static string BuildSql(string json)
{
    var parentObject = JsonConvert.DeserializeObject<JObject>(json);
    var sb = new StringBuilder();
    sb.AppendLine("where");

    void BuildSql(JObject parentObject, string path)
    {
        var lines = new List<string>();
        foreach (var prop in parentObject)
        {
            if (prop.Value is JObject childObject)
            {
                var childProps = childObject.Children().ToArray();
                if (childProps.FirstOrDefault() is JProperty firstProp && firstProp.Name.StartsWith("$"))
                {
                    var cond = firstProp.Name switch
                    {
                        "$in"  => GetContainsPredicate(path, firstProp, prop),
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

        sb.AppendLine(string.Join(" AND ", lines));
    }

    BuildSql(parentObject, "json ->");
    return sb.ToString();
}

//todo: escape this

static string GetPredicate(string path, KeyValuePair<string, JToken?> prop,JToken firstProp, string op)
{
    var val = GetPrimitive(firstProp);
    var cond = $"({path} '{prop.Key}' {op} {val})";
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

static string GetContainsPredicate(string path, JProperty firstProp, KeyValuePair<string, JToken?> prop)
{
    var x = firstProp.Value as JArray;
    var values = x.Cast<JValue>();
    var values2 = values.Select(GetPrimitive);
    var cond = $"({path} '{prop.Key}' CONTAINS ({string.Join(", ", values2)}))";
    return cond;
}