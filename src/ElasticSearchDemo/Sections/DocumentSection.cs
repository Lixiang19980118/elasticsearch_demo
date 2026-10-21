using Elastic.Clients.Elasticsearch;

namespace ElasticSearchDemo.Sections;

/// <summary>
/// 3. 文档 CRUD：新增 / 查询 / 判断存在 / 局部更新 / 脚本更新 / 删除。
/// 对应参考文档里的「文档操作 / 新建文档 / 查看文档 / 修改文档 / 删除文档」。
/// </summary>
public static class DocumentSection
{
    private const string Index = IndexSection.BooksIndex;

    public static async Task RunAsync(ElasticsearchClient client)
    {
        Demo.Section("文档 CRUD（增 / 查 / 改 / 删）");

        // ---------- 1) 新增文档：指定 id ----------
        Demo.Step($"新增文档（PUT /{Index}/_doc/1）");
        Demo.Info("8.x 客户端的 IndexAsync 重载：IndexAsync(document, index, id)");
        Demo.Info("返回的 Result 是枚举：Created / Updated / Deleted / NoOp / NotFound");

        var book1 = new Book
        {
            Id = "1",
            Title = "Elasticsearch: The Definitive Guide",
            Author = "Clinton Gormley",
            Summary = "A complete guide to Elasticsearch, covering search, analytics and cluster management.",
            Category = "search-engine",
            Tags = ["elasticsearch", "lucene", "search"],
            Price = 49.9,
            Stock = 120,
            PublishDate = new DateTimeOffset(2015, 2, 7, 0, 0, 0, TimeSpan.Zero),
            InStock = true,
        };

        var index1 = await client.IndexAsync(book1, Index, book1.Id);
        Demo.Check(index1, "新增文档 1");
        Demo.Info($"结果 {index1.Result}，_id = {index1.Id}，_version = {index1.Version}");

        // ---------- 2) 不指定 id，让 ES 生成 ----------
        Demo.Step("新增文档（不指定 id，由 ES 生成随机 id）");
        var autoIdBook = new Book
        {
            Title = "Elasticsearch in Action",
            Author = "Radu Gheorghe",
            Summary = "Practical recipes for building search applications with Elasticsearch.",
            Category = "search-engine",
            Tags = ["elasticsearch", "practical"],
            Price = 39.5,
            Stock = 80,
            PublishDate = new DateTimeOffset(2015, 12, 1, 0, 0, 0, TimeSpan.Zero),
            InStock = true,
        };

        // 不指定 id 时要走 Descriptor 重载：泛型的 IndexAsync(document, index) 并不存在
        var indexAuto = await client.IndexAsync(autoIdBook, i => i.Index(Index));
        Demo.Check(indexAuto, "新增文档（自动 id）");
        Demo.Info($"ES 生成的 _id = {indexAuto.Id}");
        var autoId = indexAuto.Id;

        // ---------- 3) 按 id 查询文档：GET /books/_doc/1 ----------
        Demo.Step($"按 id 查询（GET /{Index}/_doc/1，client.GetAsync<Book>）");
        var get = await client.GetAsync<Book>(Index, "1");
        Demo.Check(get, "查询文档 1");
        Demo.Info($"found = {get.Found}，_index = {get.Index}，_version = {get.Version}");
        if (get.Source is { } doc)
        {
            Demo.Info($"标题   : {doc.Title}");
            Demo.Info($"作者   : {doc.Author}");
            Demo.Info($"价格   : {doc.Price}，库存 {doc.Stock}，上架 {doc.InStock}");
            Demo.Info($"标签   : {string.Join(", ", doc.Tags)}");
            Demo.Info($"出版日 : {doc.PublishDate:yyyy-MM-dd}");
        }

        // ---------- 4) 查询不存在的文档 ----------
        Demo.Step("查询不存在的文档（id = 99999）");
        var notFound = await client.GetAsync<Book>(Index, "99999");
        Demo.Info($"IsValidResponse = {notFound.IsValidResponse}  ← HTTP 404，但客户端认为这是「正常结果」而不是异常");
        Demo.Info($"found = {notFound.Found}，Source = {(notFound.Source is null ? "null" : "有值")}");

        // ---------- 5) 判断文档是否存在：HEAD /books/_doc/1 ----------
        Demo.Step($"判断文档是否存在（HEAD /{Index}/_doc/1，client.ExistsAsync）");
        var exists1 = await client.ExistsAsync(Index, "1");
        Demo.Info($"id=1 存在吗？ {exists1.Exists}");
        var existsNone = await client.ExistsAsync(Index, "99999");
        Demo.Info($"id=99999 存在吗？ {existsNone.Exists}");

        // ---------- 6) 局部更新：只传要改的字段 ----------
        Demo.Step($"局部更新（POST /{Index}/_update/1，只改 price 和 stock）");
        Demo.Info("8.x 客户端用 UpdateAsync<TDocument, TPartialDocument>，TPartialDocument 就是「只包含要改字段」的类型。");

        var update = await client.UpdateAsync<Book, BookPartial>(Index, "1", u => u
            .Doc(new BookPartial { Price = 45.0, Stock = 99 })
            .Refresh(Refresh.WaitFor));   // 更新后立刻刷新，方便后面读到新值
        Demo.Check(update, "局部更新文档 1");
        Demo.Info($"结果 {update.Result}，_version = {update.Version}  ← 每次更新 version 都会 +1（乐观并发控制）");

        var afterUpdate = await client.GetAsync<Book>(Index, "1");
        Demo.Info($"更新后 price = {afterUpdate.Source?.Price}，stock = {afterUpdate.Source?.Stock}");

        // ---------- 7) 脚本更新：不改整个文档，直接对字段做运算 ----------
        Demo.Step($"脚本更新（painless 脚本把 stock 加 1）");
        var scriptUpdate = await client.UpdateAsync<Book, BookPartial>(Index, "1", u => u
            .Script(s => s
                .Source("ctx._source.stock += params.delta")
                .AddParam("delta", 1))
            .Refresh(Refresh.WaitFor));
        Demo.Check(scriptUpdate, "脚本更新文档 1");

        var afterScript = await client.GetAsync<Book>(Index, "1");
        Demo.Info($"脚本执行后 stock = {afterScript.Source?.Stock}");

        // ---------- 8) doc_as_upsert：文档不存在时当插入 ----------
        Demo.Step("doc_as_upsert：文档不存在时自动插入（PUT 一个 id=1000 的局部文档）");
        var upsert = await client.UpdateAsync<Book, BookPartial>(Index, "1000", u => u
            .Doc(new BookPartial { Title = "Upserted Book", Author = "nobody", Category = "demo", Price = 1.0 })
            .DocAsUpsert(true)
            .Refresh(Refresh.WaitFor));
        Demo.Check(upsert, "upsert 文档 1000");
        Demo.Info($"结果 {upsert.Result}  ← 文档原本不存在，所以是 Created 而不是 Updated");

        // ---------- 9) 删除文档 ----------
        Demo.Step($"删除文档（DELETE /{Index}/_doc/1000）");
        var delete = await client.DeleteAsync(Index, "1000");
        Demo.Check(delete, "删除文档 1000");
        Demo.Info($"结果 {delete.Result}，_version = {delete.Version}  ← Deleted 表示删除成功");

        var deletedAgain = await client.DeleteAsync(Index, "1000");
        Demo.Info($"再删一次结果 = {deletedAgain.Result}  ← NotFound，但 IsValidResponse 仍为 true");

        // 删掉自动生成 id 的那篇，保持索引干净
        await client.DeleteAsync(Index, autoId);
        await client.Indices.RefreshAsync(Index);
        Demo.Ok("已清理自动 id 文档");
    }

    /// <summary>
    /// 局部更新用的「部分文档」类型：只有需要修改的字段。
    /// 注意可空类型（double?/int?），未赋值的字段不会被序列化进请求体，
    /// 这样 ES 只会改你传的字段。
    /// </summary>
    private sealed class BookPartial
    {
        [System.Text.Json.Serialization.JsonPropertyName("title")]
        public string? Title { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("author")]
        public string? Author { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("category")]
        public string? Category { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("price")]
        public double? Price { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("stock")]
        public int? Stock { get; set; }
    }
}
