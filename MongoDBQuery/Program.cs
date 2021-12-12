using Marten;
using MongoDBQuery;
using Newtonsoft.Json;

var storeOptions = new StoreOptions();
storeOptions.Connection("Server=localhost;Port=5432;Database=dummy;Uid=dummy;Pwd=dummy;");
storeOptions.Policies.AllDocumentsAreMultiTenanted();
var store = new DocumentStore(storeOptions);
// await using (var session = store.LightweightSession())
// {
//     var user = new User
//     {
//         FirstName = "Luke", LastName = "Skywalker", Data = JsonConvert.DeserializeObject("{'foo':1, bar: [1,2,3,4,5,6,7]}")
//     };
//    
//     session.Store(user);
//
//     await session.SaveChangesAsync();
// }

/*
 this works...
select *
from mt_doc_user
where ( (data->'Data'->'bar') @> '[3,1,3]'::jsonb) 
 */
//var json = @"{$or:[{'age.year': {$gte: 21}, name: 'julio', contribs: { $in: [ 'ALGOL', 'Lisp' ]}}, {x: {$gt:0}]}";
//var json = @"{FirstName:'Luke', 'Data.foo': {$not: {$lt:0}}, 'Data.bar': {'$in': [1,2,3,4]}";
var json = @"{ 'FirstName': 'Han', 'Data.foo':1,'Data.foo': {$not: {$lt:0}, 'Data.bar': 1}";
var sql = QueryParser.ToSql(json, "data");
Console.WriteLine(sql);
await using var session2 = store.QuerySession();
var existing = await session2.QueryAsync<User>(sql);

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