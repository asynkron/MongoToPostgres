using Marten;
using MongoDBQuery;

var storeOptions = new StoreOptions();
storeOptions.Connection("Server=localhost;Port=5432;Database=dummy;Uid=dummy;Pwd=dummy;");
storeOptions.Policies.AllDocumentsAreMultiTenanted();
var store = new DocumentStore(storeOptions);
// await using (var session = store.LightweightSession())
// {
//     var user = new User
//     {
//         FirstName = "Han", LastName = "Solo", Data = JsonConvert.DeserializeObject("{'foo':1}")
//     };
//    
//     session.Store(user);
//
//     await session.SaveChangesAsync();
// }

//var json = @"{$or:[{'age.year': {$gte: 21}, name: 'julio', contribs: { $in: [ 'ALGOL', 'Lisp' ]}}, {x:123}]}";
var json = @"{FirstName:'Han', 'Data.foo': 1}";
var sql = QueryParser.ToSql(json, "data");
Console.WriteLine(sql);
await using var session = store.QuerySession();
var existing = await session.QueryAsync<User>(sql);

foreach (var res in existing)
{
    Console.WriteLine(res.Id);
}

public class User
{
    public Guid Id { get; set; }
    public string FirstName { get; set; }
    public string LastName { get; set; }
    public bool Internal { get; set; }
    public string UserName { get; set; }
    public string Department { get; set; }

    public dynamic Data { get; set; }
}