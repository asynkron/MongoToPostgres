using Marten;
using MongoDBQuery;
using Newtonsoft.Json;

var storeOptions = new StoreOptions();
storeOptions.Connection("Server=localhost;Port=5432;Database=dummy;Uid=dummy;Pwd=dummy;");
storeOptions.Policies.AllDocumentsAreMultiTenanted();
var store = new DocumentStore(storeOptions);
/*
select data
from mt_doc_user
WHERE (EXISTS(
        SELECT *
        FROM jsonb_array_elements(data->'Data'->'children') t(data)
        WHERE data -> 'a' = '1'::jsonb
    ))
 */

// await using (var session = store.LightweightSession())
// {
//     var user = new User
//     {
//         FirstName = "Roger", LastName = "Johansson", Data = JsonConvert.DeserializeObject("{'foo':1, children: [{'a':1},{a:7}]}")
//     };
//    
//     session.Store(user);
//
//     await session.SaveChangesAsync();
// }


var json = @"{ 'FirstName': ['Roger','Luke'], 'Data.foo': 1, 'Data.children' : { '$elemMatch' : { 'a':1 } }";
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