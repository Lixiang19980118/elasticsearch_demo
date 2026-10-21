using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;

namespace ElasticSearchDemo.Sections;

/// <summary>
/// 5. 检索：match_all、分页、排序、_source 过滤、match、term、terms、
///    bool（must / should / must_not / filter + range）、highlight。
/// 对应参考文档里的「检索文档 / 简单检索 / 查询表达式」。
/// </summary>
public static class SearchSection
{
    private const string Index = IndexSection.BooksIndex;

    public static async Task RunAsync(ElasticsearchClient client)
    {
        Demo.Section("检索（Query DSL）");

        // ---------- 1) match_all：查全部 ----------
        Demo.Step("match_all 查全部（GET /books/_search）");
        var all = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.MatchAll())
            .Size(3));

        Demo.Check(all, "match_all 检索");
        Demo.Info($"命中总数 = {all.Total}，本次返回 {all.Documents.Count} 条，耗时 {all.Took} ms");
        Demo.Info("注意：8.x 里 Total 默认上限是 10000，超过会显示为 10000；");
        Demo.Info("     要精确总数用 .TrackTotalHits(true)（下面会演示）。");
        PrintHits(all, "标题", b => b.Title);

        // ---------- 2) 分页 + 排序 ----------
        Demo.Step("分页 + 排序（from/size + sort price desc）");
        var paged = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.MatchAll())
            .From(0)
            .Size(3)
            .Sort(sort => sort.Field(b => b.Price, SortOrder.Desc))
            .TrackTotalHits(true));

        Demo.Check(paged, "分页排序检索");
        Demo.Info($"命中总数 = {paged.Total}（TrackTotalHits(true) 拿到精确值）");
        Demo.Info("按价格从高到低，第 1 页：");
        PrintHits(paged, "价格", b => $"{b.Title} —— {b.Price}");

        var page2 = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.MatchAll())
            .From(3)
            .Size(3)
            .Sort(sort => sort.Field(b => b.Price, SortOrder.Desc)));

        Demo.Info("第 2 页：");
        PrintHits(page2, "价格", b => $"{b.Title} —— {b.Price}");

        // ---------- 3) _source 过滤：只返回需要的字段 ----------
        Demo.Step("_source 过滤（只返回需要的字段，减小响应体）");
        Demo.Info("8.x 客户端里有两种写法，效果一样，但走的通道不同：");
        Demo.Info("  ① SourceIncludes/SourceExcludes —— 走 URL 查询串：?_source_includes=title,price");
        Demo.Info("  ② Source(sf => sf.Filter(...))  —— 走请求体：{\"_source\": {\"includes\": ...}}");

        var sourceFiltered = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.MatchAll())
            .Size(2)
            .SourceIncludes(b => b.Title, b => b.Price)
            .SourceExcludes(b => b.Summary));

        Demo.Check(sourceFiltered, "SourceIncludes/SourceExcludes 检索");
        Demo.Info($"它的请求地址：{sourceFiltered.ApiCallDetails?.Uri}");
        Demo.Info("可以看到过滤条件在查询串里，所以只看请求体会以为没生效 —— 这点容易踩坑。");
        Demo.Info("返回的文档里只有 title / price，其他字段是默认值：");
        foreach (var hit in sourceFiltered.Hits)
        {
            Demo.Info($"  _id={hit.Id}  title=\"{hit.Source?.Title}\"  price={hit.Source?.Price}  author=\"{hit.Source?.Author}\"（未返回，所以为空）");
        }
        Demo.Json("它的请求体（注意体里没有 _source）", Demo.RawBody(sourceFiltered));

        Demo.Info("写法②把条件放进请求体，和旧教程的 JSON 一一对应：");
        var sourceFiltered2 = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.MatchAll())
            .Size(1)
            .Source(sf => sf.Filter(f => f.Includes(b => b.Title).Excludes(b => b.Summary))));
        Demo.Check(sourceFiltered2, "Source(Filter) 检索");
        Demo.Json("请求体（对照旧教程的 {\"_source\": {\"includes\": [...], \"excludes\": [...]}}）",
            Demo.RawBody(sourceFiltered2));
        Demo.Info("同样只返回了 title，excludes 的 summary 也没了。");

        // ---------- 4) match：全文检索 ----------
        Demo.Step("match 全文检索（title 里含 guide）");
        Demo.Info("match 会先对查询词做分词，再和字段的倒排索引匹配，是相关性打分查询。");

        var match = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.Match(m => m
                .Field(b => b.Title)
                .Query("guide")))
            .Size(5));

        Demo.Check(match, "match 检索");
        Demo.Info($"命中 {match.Total} 条：");
        foreach (var hit in match.Hits)
        {
            Demo.Info($"  [{hit.Score:0.000}] {hit.Source?.Title}");
        }

        Demo.Info("加 operator=and：要求所有词都命中（对比一下命中数的变化）");
        var matchAnd = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.Match(m => m
                .Field(b => b.Summary)
                .Query("search library")
                .Operator(Operator.And))));
        Demo.Info($"  summary 同时含 search 和 library 的文档数 = {matchAnd.Total}");

        var matchOr = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.Match(m => m
                .Field(b => b.Summary)
                .Query("search library")
                .Operator(Operator.Or))));
        Demo.Info($"  summary 含 search 或 library 的文档数 = {matchOr.Total}");

        // ---------- 5) 多字段检索 ----------
        Demo.Step("multi_match：一次在多个字段里检索");
        var multi = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.MultiMatch(m => m
                .Query("distributed")
                .Fields(new[] { "title", "summary", "tags" })))
            .Size(5));
        Demo.Check(multi, "multi_match 检索");
        foreach (var hit in multi.Hits)
        {
            Demo.Info($"  [{hit.Score:0.000}] {hit.Source?.Title}  ({hit.Source?.Category})");
        }

        // ---------- 6) term：精确匹配，不分词 ----------
        Demo.Step("term 精确匹配（category = search-engine）");
        Demo.Info("term 不分析查询词，直接拿词条去倒排索引里查，只适合 keyword 类型字段。");
        Demo.Info("对 text 字段用 term 常常查不到东西 —— 因为写入时被分词了。");

        var term = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.Term(t => t
                .Field(b => b.Category)
                .Value("search-engine")))
            .Size(5));
        Demo.Check(term, "term 检索");
        PrintHits(term, "分类", b => $"{b.Title} —— {b.Category}");

        // ---------- 7) terms：多个值任意匹配（相当于 SQL 的 IN） ----------
        Demo.Step("terms（相当于 SQL 的 IN）");
        var terms = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.Terms(t => t
                .Field(b => b.Category)
                .Terms(new TermsQueryField([
                    FieldValue.String("architecture"),
                    FieldValue.String("operations"),
                ]))))
            .Size(10));
        Demo.Check(terms, "terms 检索");
        PrintHits(terms, "分类", b => $"{b.Title} —— {b.Category}");

        // ---------- 8) range：范围查询 ----------
        Demo.Step("range 范围查询（价格 40 ~ 55 之间，库存大于 0）");
        var range = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.Bool(b => b
                .Filter(f => f.Range(r => r.Number(n => n
                    .Field(book => book.Price)
                    .Gte(40)
                    .Lte(55))),
                    f => f.Range(r => r.Number(n => n
                        .Field(book => book.Stock)
                        .Gt(0))))))
            .Size(10)
            .Sort(sort => sort.Field(b => b.Price, SortOrder.Asc)));

        Demo.Check(range, "range 检索");
        Demo.Info("filter 上下文不参与打分，结果可以缓存，比 must 更省资源。");
        PrintHits(range, "价格/库存", b => $"{b.Title} —— {b.Price} / {b.Stock}");

        // 日期范围
        Demo.Step("日期范围查询（2015 年之后出版）");
        var dateRange = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.Range(r => r.Date(d => d
                .Field(b => b.PublishDate)
                .Gte("2015-01-01"))))
            .Size(20)
            .Sort(sort => sort.Field(b => b.PublishDate, SortOrder.Asc)));
        Demo.Check(dateRange, "日期范围检索");
        PrintHits(dateRange, "出版日", b => $"{b.Title} —— {b.PublishDate:yyyy-MM-dd}");

        // ---------- 9) bool：must / should / must_not / filter 组合 ----------
        Demo.Step("bool 组合查询：must + should + must_not + filter");
        Demo.Info("must     : 必须匹配，参与打分");
        Demo.Info("filter   : 必须匹配，不参与打分（可缓存）");
        Demo.Info("should   : 应该匹配，匹配到的打分更高（默认至少命中一个）");
        Demo.Info("must_not : 必须不匹配");

        var boolQuery = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.Bool(b => b
                .Must(m => m.Match(mm => mm.Field(book => book.Summary).Query("search")))
                .Filter(f => f.Range(r => r.Number(n => n.Field(book => book.Price).Gte(30))))
                .Should(sh => sh.Term(t => t.Field(book => book.Category).Value("search-engine")))
                .MustNot(mn => mn.Term(t => t.Field(book => book.InStock).Value(false)))))
            .Size(10));

        Demo.Check(boolQuery, "bool 组合检索");
        Demo.Info("解读：summary 里要出现 search；价格 >= 30；不包含已下架的书；");
        Demo.Info("      category 是 search-engine 的排前面（should 提高得分）。");
        PrintHits(boolQuery, "得分", b => $"{b.Title} —— {b.Category} —— 在售 {b.InStock}");

        // ---------- 10) 高亮 ----------
        Demo.Step("highlight：把命中的关键词标出来");
        var highlighted = await client.SearchAsync<Book>(Index, s => s
            .Query(q => q.Match(m => m.Field(b => b.Summary).Query("search")))
            .Size(2)
            .Highlight(h => h
                .PreTags("<em>")
                .PostTags("</em>")
                .Fields(f => f.Add(b => b.Summary, hf => hf.NumberOfFragments(1)))));

        Demo.Check(highlighted, "高亮检索");
        foreach (var hit in highlighted.Hits)
        {
            Demo.Info($"  {hit.Source?.Title}");
            if (hit.Highlight is not null && hit.Highlight.TryGetValue("summary", out var fragments))
            {
                foreach (var fragment in fragments)
                {
                    Demo.Info($"    …{fragment}…");
                }
            }
        }

        // ---------- 11) 统计总数 ----------
        Demo.Step("只统计数量（_count，比 _search 更轻量）");
        var count = await client.CountAsync<Book>(Index, c => c
            .Query(q => q.Term(t => t.Field(b => b.Category).Value("search-engine"))));
        Demo.Check(count, "count 统计");
        Demo.Info($"category=search-engine 的文档数 = {count.Count}");
    }

    /// <summary>把一次检索的命中列表按给定格式打印出来。</summary>
    private static void PrintHits(SearchResponse<Book> response, string label, Func<Book, string> selector)
    {
        foreach (var hit in response.Hits)
        {
            if (hit.Source is { } doc)
            {
                Demo.Info($"  {hit.Id,-5} {selector(doc)}");
            }
        }

        if (response.Hits.Count == 0)
        {
            Demo.Info($"  （没有命中）");
        }
    }
}
