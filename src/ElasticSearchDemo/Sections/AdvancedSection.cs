using System.Text;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Esql;

namespace ElasticSearchDemo.Sections;

/// <summary>
/// 7. 进阶：分词器验证（_analyze）、按查询删除/更新（_delete_by_query / _update_by_query）、
///    别名（alias）、ES|QL（8.11+ 新特性）。
/// 对应参考文档里的「分词 / 复杂查询」部分，并补充 8.x 新东西。
/// </summary>
public static class AdvancedSection
{
    private const string Index = IndexSection.BooksIndex;

    public static async Task RunAsync(ElasticsearchClient client)
    {
        Demo.Section("进阶：分词、按查询删改、别名、ES|QL");

        // ---------- 1) 分词器：看清楚一句话被切成了什么 ----------
        Demo.Step("验证分词器（POST /books/_analyze）");
        Demo.Info("检索结果对不对，八成取决于分词器。调试查询前先 _analyze 看一眼词条。");

        // 索引里定义的 book_analyzer = standard 分词 + lowercase + asciifolding
        Demo.Info("① 用索引里定义的自定义分词器 book_analyzer（standard + lowercase + asciifolding）：");
        var analyze1 = await client.Indices.AnalyzeAsync(Index, a => a
            .Analyzer("book_analyzer")
            .Text("The QUICK Brown Foxes Jumped over 2 Lazy DOGS"));
        Demo.Check(analyze1, "分词（自定义分词器）");
        Demo.Info($"   原文 → {analyze1.Tokens.Count} 个词条：");
        Demo.Info($"   {string.Join(" | ", analyze1.Tokens.Select(t => t.Token))}");
        Demo.Info("   注意：lowercase 只负责转小写，并不做词干还原 —— foxes 还是 foxes。");
        Demo.Info("   想要词干还原（foxes→fox、lazy→lazi）得用 porter_stem 或内置的 english 分词器。");

        Demo.Info("② 用内置 standard 分词器对比（不转小写、不去音标）：");
        var analyze2 = await client.Indices.AnalyzeAsync(Index, a => a
            .Analyzer("standard")
            .Text("The QUICK Brown Foxes Jumped over 2 Lazy DOGS"));
        Demo.Info($"   {string.Join(" | ", analyze2.Tokens.Select(t => t.Token))}");

        Demo.Info("③ 用内置 english 分词器对比（会做词干还原 + 去停用词）：");
        var analyze3 = await client.Indices.AnalyzeAsync(Index, a => a
            .Analyzer("english")
            .Text("The QUICK Brown Foxes Jumped over 2 Lazy DOGS"));
        Demo.Info($"   {string.Join(" | ", analyze3.Tokens.Select(t => t.Token))}");

        Demo.Info("④ asciifolding 的作用：把带音标的字符转成 ASCII，搜 cafe 也能命中 café。");
        var analyze4 = await client.Indices.AnalyzeAsync(Index, a => a
            .Analyzer("book_analyzer")
            .Text("Café naïve Résumé"));
        Demo.Info($"   book_analyzer → {string.Join(" | ", analyze4.Tokens.Select(t => t.Token))}");
        var analyze4b = await client.Indices.AnalyzeAsync(Index, a => a
            .Analyzer("standard")
            .Text("Café naïve Résumé"));
        Demo.Info($"   standard      → {string.Join(" | ", analyze4b.Tokens.Select(t => t.Token))}");

        Demo.Info("⑤ 中文分词：standard 会把汉字拆成单字，chinese_analyzer（加 cjk_bigram）会组成双字词。");
        var analyzeCn1 = await client.Indices.AnalyzeAsync(Index, a => a
            .Analyzer("standard")
            .Text("Elasticsearch 权威指南：全文搜索引擎"));
        Demo.Info($"   standard        → {string.Join(" | ", analyzeCn1.Tokens.Select(t => t.Token))}");
        var analyzeCn2 = await client.Indices.AnalyzeAsync(Index, a => a
            .Analyzer("chinese_analyzer")
            .Text("Elasticsearch 权威指南：全文搜索引擎"));
        Demo.Info($"   chinese_analyzer → {string.Join(" | ", analyzeCn2.Tokens.Select(t => t.Token))}");
        Demo.Info("   所以中文检索要么用 bigram（能凑合），要么上 analysis-ik 这类词典分词插件。");

        Demo.Info("⑥ 用 keyword 分词器：整句当一个词条（keyword 字段就是这么存的）：");
        var analyze5 = await client.Indices.AnalyzeAsync(Index, a => a
            .Analyzer("keyword")
            .Text("Elasticsearch 权威指南"));
        Demo.Info($"   {string.Join(" | ", analyze5.Tokens.Select(t => t.Token))}");

        Demo.Info("⑦ 临时指定分词器和过滤器（不改索引设置也能试）：");
        var analyze6 = await client.Indices.AnalyzeAsync(Index, a => a
            .Tokenizer(t => t.Standard())
            .Filter(f => f.Lowercase(), f => f.Stop())
            .Text("The quick brown fox jumps over the lazy dog"));
        Demo.Info($"   {string.Join(" | ", analyze6.Tokens.Select(t => t.Token))}");
        Demo.Info("   （the / over 这类停用词被 stop 过滤器去掉了，所以位置号有跳号）");

        Demo.Info("⑧ 直接分析某个已有字段的取值（用该字段配置的分词器）：");
        var analyze7 = await client.Indices.AnalyzeAsync(Index, a => a
            .Field("title")
            .Text("Lucene in Action"));
        Demo.Info($"   {string.Join(" | ", analyze7.Tokens.Select(t => t.Token))}");

        // ---------- 2) 按查询更新 ----------
        Demo.Step("按查询更新（POST /books/_update_by_query）");
        Demo.Info("把 category=operations 的书全部打 9 折，不用先把文档拉回本地再写回去。");

        var updateByQuery = await client.UpdateByQueryAsync<Book>(Index, u => u
            .Query(q => q.Term(t => t.Field(b => b.Category).Value("operations")))
            .Script(s => s.Source("ctx._source.price = Math.round(ctx._source.price * params.rate * 100) / 100.0")
                .AddParam("rate", 0.9))
            .Refresh(true));

        Demo.Check(updateByQuery, "按查询更新");
        Demo.Info($"扫描 {updateByQuery.Total} 条，更新 {updateByQuery.Updated} 条，耗时 {updateByQuery.Took}");

        var afterUpdate = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.Term(t => t.Field(b => b.Category).Value("operations")))
            .Size(5));
        foreach (var hit in afterUpdate.Hits)
        {
            Demo.Info($"  {hit.Source?.Title} —— 现价 {hit.Source?.Price}");
        }

        // ---------- 3) 按查询删除 ----------
        Demo.Step("按查询删除（POST /books/_delete_by_query）");
        Demo.Info("先把 BulkAll 生成的样本数据删掉，再演示真正的删除。");

        var cleanup = await client.DeleteByQueryAsync<Book>(d => d
            .Indices(Index)
            .Query(q => q.Term(t => t.Field(b => b.Tags).Value("bulkall")))
            .Refresh(true));
        Demo.Check(cleanup, "清理 BulkAll 样本数据");
        Demo.Info($"删除 {cleanup.Deleted} 条，剩余文档总数 = {(await client.CountAsync<Book>(c => c.Indices(Index))).Count}");

        Demo.Info("现在删掉所有缺货的书（in_stock = false）：");
        var deleteOutOfStock = await client.DeleteByQueryAsync<Book>(d => d
            .Indices(Index)
            .Query(q => q.Term(t => t.Field(b => b.InStock).Value(false)))
            .Refresh(true));
        Demo.Check(deleteOutOfStock, "删除缺货图书");
        Demo.Info($"删除 {deleteOutOfStock.Deleted} 条，剩余文档总数 = {(await client.CountAsync<Book>(c => c.Indices(Index))).Count}");

        // ---------- 4) 别名 ----------
        Demo.Step("索引别名（alias）：给索引起个逻辑名字，读写可以指向不同索引");
        Demo.Info("好处：换索引（重建/扩容）时只改别名，应用代码不用动。");

        var alias = await client.Indices.UpdateAliasesAsync(a => a
            .Actions(act => act.Add(add => add
                .Index(Index)
                .Aliases(new[] { (IndexAlias)"books_current" }))));
        Demo.Check(alias, "添加别名 books_current");

        // 通过别名检索，和直接用索引名效果一样
        var viaAlias = await client.SearchAsync<Book>("books_current", s => s
            .Query(q => q.MatchAll())
            .Size(1)
            .TrackTotalHits(true));
        Demo.Info($"通过别名 books_current 检索，命中 {viaAlias.Total} 条");

        var aliasMapping = await client.Indices.GetMappingAsync("books_current");
        Demo.Info($"别名指向的索引：{string.Join(", ", aliasMapping.Mappings.Keys)}");

        var removeAlias = await client.Indices.UpdateAliasesAsync(a => a
            .Actions(act => act.Remove(rm => rm
                .Index(Index)
                .Aliases(new[] { (IndexAlias)"books_current" }))));
        Demo.Check(removeAlias, "移除别名 books_current");

        // ---------- 5) ES|QL ----------
        Demo.Step("ES|QL（8.11+ 新增的管道查询语言，语法类似 SPL / KQL）");
        Demo.Info("用 | 串联：FROM ... | WHERE ... | STATS ... BY ... | SORT ... | LIMIT ...");
        Demo.Info("注意：ES|QL 返回的是表格（列 + 行），不是普通 _search 的 JSON 结构。");

        Demo.Info("① 简单查询：");
        var esql1 = await client.Esql.QueryAsync(q => q
            .Query($"FROM {Index} | KEEP title, category, price | SORT price DESC | LIMIT 5")
            .Format(EsqlFormat.Json));
        Demo.Check(esql1, "ES|QL 查询");
        Demo.Json("ES|QL 结果", await ReadStreamAsync(esql1));

        Demo.Info("② 聚合查询（等价于 terms + avg 聚合，但写起来像 SQL）：");
        var esql2 = await client.Esql.QueryAsync(q => q
            .Query($"FROM {Index} | STATS count = COUNT(*), avg_price = AVG(price) BY category | SORT count DESC")
            .Format(EsqlFormat.Json));
        Demo.Check(esql2, "ES|QL 聚合");
        Demo.Json("ES|QL 聚合结果", await ReadStreamAsync(esql2));

        Demo.Info("③ 也可以直接映射到强类型对象（QueryAsObjectsAsync）：");
        var rows = await client.Esql.QueryAsObjectsAsync<EsqlBookRow>(q => q
            .Query($"FROM {Index} | KEEP title, price | SORT price DESC | LIMIT 3"));
        foreach (var row in rows)
        {
            Demo.Info($"  {row.Title} —— {row.Price}");
        }
    }

    /// <summary>把流式响应读成字符串，用于打印原始 JSON。</summary>
    private static async Task<string?> ReadStreamAsync(Elastic.Transport.Products.Elasticsearch.ElasticsearchStreamResponse response)
    {
        if (!response.IsValidResponse || response.Body is null)
        {
            return null;
        }

        using var reader = new StreamReader(response.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    /// <summary>ES|QL 结果映射用的行类型。</summary>
    private sealed class EsqlBookRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("price")]
        public double Price { get; set; }
    }
}
