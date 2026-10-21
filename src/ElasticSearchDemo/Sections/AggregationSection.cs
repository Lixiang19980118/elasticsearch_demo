using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;

namespace ElasticSearchDemo.Sections;

/// <summary>
/// 6. 聚合分析：terms、terms + avg、range + terms + avg、stats、cardinality、date_histogram。
/// 对应参考文档里的「聚合 / 分组统计」。
/// </summary>
public static class AggregationSection
{
    private const string Index = IndexSection.BooksIndex;

    public static async Task RunAsync(ElasticsearchClient client)
    {
        Demo.Section("聚合分析（Aggregations）");

        // ---------- 1) terms：按分类分组计数 ----------
        Demo.Step("terms 聚合：按 category 分组统计文档数");
        Demo.Info("等价于 SQL：SELECT category, COUNT(*) FROM books GROUP BY category");
        Demo.Info("注意 terms 聚合要作用在 keyword 字段上（text 字段默认没有 doc_values，不能聚合）。");

        var byCategory = await client.SearchAsync<Book>(Index, s => s
            .Size(0)   // 只要聚合结果，不要文档，省带宽
            .Aggregations(a => a
                .Add("by_category", ag => ag.Terms(t => t
                    .Field(b => b.Category)
                    .Size(10)))));

        Demo.Check(byCategory, "terms 聚合");
        Demo.Json("请求体", Demo.RawBody(byCategory));

        var categories = byCategory.Aggregations!.GetStringTerms("by_category");
        Demo.Info($"分类数 = {categories.Buckets.Count}，未归类文档数 = {categories.SumOtherDocCount}");
        foreach (var bucket in categories.Buckets)
        {
            Demo.Info($"  {bucket.Key,-16} {bucket.DocCount,3} 本");
        }

        // ---------- 2) terms + 子聚合：每个分类的平均价格 ----------
        Demo.Step("terms 聚合 + 子聚合 avg：每个分类的平均价格");
        Demo.Info("聚合可以嵌套：外层按 category 分桶，每个桶里再算 avg(price)。");

        var avgPriceByCategory = await client.SearchAsync<Book>(Index, s => s
            .Size(0)
            .Aggregations(a => a
                .Add("by_category", ag => ag.Terms(t => t
                    .Field(b => b.Category)
                    .Size(10))
                    .Aggregations(sub => sub
                        .Add("avg_price", inner => inner.Avg(av => av.Field(b => b.Price)))
                        .Add("max_price", inner => inner.Max(mx => mx.Field(b => b.Price)))
                        .Add("min_price", inner => inner.Min(mn => mn.Field(b => b.Price)))
                        .Add("total_stock", inner => inner.Sum(sm => sm.Field(b => b.Stock)))))));

        Demo.Check(avgPriceByCategory, "terms + avg 聚合");
        var grouped = avgPriceByCategory.Aggregations!.GetStringTerms("by_category");
        Demo.Info($"{"分类",-16}{"数量",6}{"均价",10}{"最高",10}{"最低",10}{"总库存",10}");
        foreach (var bucket in grouped.Buckets)
        {
            var avg = bucket.Aggregations?.GetAverage("avg_price")?.Value ?? 0;
            var max = bucket.Aggregations?.GetMax("max_price")?.Value ?? 0;
            var min = bucket.Aggregations?.GetMin("min_price")?.Value ?? 0;
            var stock = bucket.Aggregations?.GetSum("total_stock")?.Value ?? 0;
            Demo.Info($"  {bucket.Key,-14}{bucket.DocCount,6}{avg,10:0.00}{max,10:0.0}{min,10:0.0}{stock,10:0}");
        }

        // ---------- 3) 按价格区间分桶，再算每档的均价 ----------
        Demo.Step("range 聚合 + 子聚合：按价格区间分桶，统计每档数量和均价");
        Demo.Info("等价于 SQL：SELECT CASE WHEN ... END, COUNT(*), AVG(price) ... GROUP BY 价格档");

        var byPriceRange = await client.SearchAsync<Book>(Index, s => s
            .Size(0)
            .Aggregations(a => a
                .Add("price_ranges", ag => ag.Range(r => r
                    .Field(b => b.Price)
                    .Ranges(
                        new AggregationRange { Key = "便宜 (<30)", To = 30 },
                        new AggregationRange { Key = "中等 (30-50)", From = 30, To = 50 },
                        new AggregationRange { Key = "偏贵 (>=50)", From = 50 }))
                    .Aggregations(sub => sub
                        .Add("avg_price", inner => inner.Avg(av => av.Field(b => b.Price)))
                        .Add("avg_stock", inner => inner.Avg(av => av.Field(b => b.Stock)))))));

        Demo.Check(byPriceRange, "range 聚合");
        var ranges = byPriceRange.Aggregations!.GetRange("price_ranges");
        foreach (var bucket in ranges.Buckets)
        {
            var avg = bucket.Aggregations?.GetAverage("avg_price")?.Value ?? 0;
            var avgStock = bucket.Aggregations?.GetAverage("avg_stock")?.Value ?? 0;
            Demo.Info($"  {bucket.Key,-16} {bucket.DocCount,3} 本   均价 {avg,7:0.00}   平均库存 {avgStock,7:0.0}");
        }

        // ---------- 4) 一次请求拿多个聚合 ----------
        Demo.Step("一次请求返回多个聚合指标（stats / cardinality / value_count）");
        var multi = await client.SearchAsync<Book>(Index, s => s
            .Size(0)
            .Aggregations(a => a
                .Add("price_stats", ag => ag.Stats(st => st.Field(b => b.Price)))
                .Add("stock_stats", ag => ag.Stats(st => st.Field(b => b.Stock)))
                .Add("category_count", ag => ag.Cardinality(c => c.Field(b => b.Category)))
                .Add("author_count", ag => ag.Cardinality(c => c.Field(b => b.Author)))
                .Add("book_count", ag => ag.ValueCount(v => v.Field(b => b.Id)))));

        Demo.Check(multi, "多指标聚合");
        var priceStats = multi.Aggregations!.GetStats("price_stats");
        Demo.Info("价格统计（stats 一次性给出 count/min/max/avg/sum）：");
        Demo.Info($"  数量 {priceStats.Count}，最小 {priceStats.Min:0.00}，最大 {priceStats.Max:0.00}，平均 {priceStats.Avg:0.00}，合计 {priceStats.Sum:0.00}");

        var stockStats = multi.Aggregations.GetStats("stock_stats");
        Demo.Info($"库存统计：最小 {stockStats.Min:0}，最大 {stockStats.Max:0}，平均 {stockStats.Avg:0.00}");

        Demo.Info($"分类去重个数（cardinality）   = {multi.Aggregations.GetCardinality("category_count").Value}");
        Demo.Info($"作者去重个数（cardinality）   = {multi.Aggregations.GetCardinality("author_count").Value}");
        Demo.Info($"有 id 字段的文档数（value_count） = {multi.Aggregations.GetValueCount("book_count").Value}");

        // ---------- 5) 先过滤再聚合 ----------
        Demo.Step("带查询条件的聚合（先筛选再分组）");
        var filtered = await client.SearchAsync<Book>(Index, s => s
            .Size(0)
            .Query(q => q.Range(r => r.Number(n => n.Field(b => b.Price).Gte(40))))
            .Aggregations(a => a
                .Add("by_category", ag => ag.Terms(t => t.Field(b => b.Category).Size(10))
                    .Aggregations(sub => sub
                        .Add("avg_price", inner => inner.Avg(av => av.Field(b => b.Price)))))));

        Demo.Check(filtered, "条件聚合");
        Demo.Info("只看价格 >= 40 的书，再按分类分组：");
        foreach (var bucket in filtered.Aggregations!.GetStringTerms("by_category").Buckets)
        {
            var avg = bucket.Aggregations?.GetAverage("avg_price")?.Value ?? 0;
            Demo.Info($"  {bucket.Key,-16} {bucket.DocCount,3} 本   均价 {avg,7:0.00}");
        }

        // ---------- 6) filter 聚合：把不同条件放进一个请求 ----------
        Demo.Step("filter / global 聚合：一次请求对比「有货」和「缺货」");
        var filterAgg = await client.SearchAsync<Book>(Index, s => s
            .Size(0)
            .Query(q => q.Term(t => t.Field(b => b.Category).Value("search-engine")))
            .Aggregations(a => a
                .Add("in_stock", ag => ag.Filter(f => f.Term(t => t.Field(b => b.InStock).Value(true))))
                .Add("out_of_stock", ag => ag.Filter(f => f.Term(t => t.Field(b => b.InStock).Value(false))))
                .Add("all_books", ag => ag.Global())));

        Demo.Check(filterAgg, "filter/global 聚合");
        Demo.Info($"search-engine 分类下有货的  = {filterAgg.Aggregations!.GetFilter("in_stock").DocCount}");
        Demo.Info($"search-engine 分类下缺货的  = {filterAgg.Aggregations.GetFilter("out_of_stock").DocCount}");
        Demo.Info($"global 聚合忽略查询条件，全索引文档数 = {filterAgg.Aggregations.GetGlobal("all_books").DocCount}");

        // ---------- 7) date_histogram：按时间分桶 ----------
        Demo.Step("date_histogram 聚合：按年统计出版数量");
        var byYear = await client.SearchAsync<Book>(Index, s => s
            .Size(0)
            .Aggregations(a => a
                .Add("books_per_year", ag => ag.DateHistogram(dh => dh
                    .Field(b => b.PublishDate)
                    .CalendarInterval(CalendarInterval.Year)
                    .Format("yyyy")
                    .MinDocCount(1)))));

        Demo.Check(byYear, "date_histogram 聚合");
        foreach (var bucket in byYear.Aggregations!.GetDateHistogram("books_per_year").Buckets)
        {
            Demo.Info($"  {bucket.KeyAsString ?? bucket.Key.ToString("yyyy")}  {new string('█', (int)bucket.DocCount)} {bucket.DocCount}");
        }

        // ---------- 8) 按嵌套字段的聚合（tags 是数组） ----------
        Demo.Step("对数组字段聚合：统计每个标签出现多少次");
        var byTag = await client.SearchAsync<Book>(Index, s => s
            .Size(0)
            .Aggregations(a => a
                .Add("by_tag", ag => ag.Terms(t => t
                    .Field(b => b.Tags)
                    .Size(10)
                    .Order(new[] { new KeyValuePair<Field, SortOrder>("_count", SortOrder.Desc) })))));

        Demo.Check(byTag, "tags 聚合");
        foreach (var bucket in byTag.Aggregations!.GetStringTerms("by_tag").Buckets)
        {
            Demo.Info($"  {bucket.Key,-16} {bucket.DocCount,3} 次");
        }
    }
}
