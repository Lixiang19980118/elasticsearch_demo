using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Aggregations;

namespace ElasticSearchDemo.Sections;

/// <summary>
/// 8. 完整示例：用 bank 账户数据走一遍「建索引 → 造数据 → 多维检索 → 复杂聚合」。
/// 参考文档里最后的聚合例子用的就是这套官方示例数据。
/// </summary>
public static class BankSection
{
    private const string Index = "bank";

    public static async Task RunAsync(ElasticsearchClient client)
    {
        Demo.Section("完整示例：bank 账户数据的检索与聚合");

        // ---------- 1) 重建索引 ----------
        Demo.Step($"重建索引 {Index}（自定义 mapping，明确每个字段类型）");
        if ((await client.Indices.ExistsAsync(Index)).Exists)
        {
            await client.Indices.DeleteAsync(Index);
        }

        var create = await client.Indices.CreateAsync<BankAccount>(Index, c => c
            .Settings(s => s.NumberOfShards(1).NumberOfReplicas(0))
            .Mappings(m => m.Properties(p => p
                .IntegerNumber(a => a.AccountNumber)
                .Keyword(a => a.FirstName)
                .Keyword(a => a.LastName)
                .IntegerNumber(a => a.Age)
                .Keyword(a => a.Gender)
                .Keyword(a => a.City)
                .Keyword(a => a.State)
                .DoubleNumber(a => a.Balance)
                .Keyword(a => a.Employer)
                .Keyword(a => a.Email)
                .Date(a => a.CreatedAt))));

        Demo.Check(create, $"创建索引 {Index}");

        // ---------- 2) 造数据 ----------
        Demo.Step("批量写入 200 条账户数据（Bulk）");
        var accounts = GenerateAccounts(200);
        var bulk = await client.BulkAsync(b => b
            .Index(Index)
            .IndexMany(accounts, (op, a) => op.Id(a.AccountNumber.ToString()))
            .Refresh(Refresh.WaitFor));

        Demo.Check(bulk, "批量写入账户数据");
        Demo.Info($"写入 {bulk.Items.Count} 条，errors = {bulk.Errors}");

        // ---------- 3) 检索 ----------
        Demo.Step("检索：年龄 30~40 岁、余额大于 20000 的账户，按余额降序");
        var search = await client.SearchAsync<BankAccount>(Index, s => s
            .Query(q => q.Bool(b => b
                .Must(m => m.Range(r => r.Number(n => n.Field(a => a.Age).Gte(30).Lte(40))))
                .Filter(f => f.Range(r => r.Number(n => n.Field(a => a.Balance).Gt(20000))))))
            .Sort(sort => sort.Field(a => a.Balance, SortOrder.Desc))
            .Size(5)
            .TrackTotalHits(true));

        Demo.Check(search, "条件检索");
        Demo.Info($"命中 {search.Total} 条，取前 5 条：");
        foreach (var hit in search.Hits)
        {
            if (hit.Source is { } acc)
            {
                Demo.Info($"  #{acc.AccountNumber,-4} {acc.FirstName} {acc.LastName,-12} {acc.Age} 岁  {acc.City,-14} 余额 {acc.Balance,12:N2}");
            }
        }

        // ---------- 4) 聚合 ----------
        Demo.Step("聚合①：按州分组，统计账户数和平均余额，取前 5 个州");
        var byState = await client.SearchAsync<BankAccount>(Index, s => s
            .Size(0)
            .Aggregations(a => a
                .Add("by_state", ag => ag.Terms(t => t
                    .Field(x => x.State)
                    .Size(5)
                    .Order(new[] { new KeyValuePair<Field, SortOrder>("avg_balance", SortOrder.Desc) }))
                    .Aggregations(sub => sub
                        .Add("avg_balance", inner => inner.Avg(av => av.Field(x => x.Balance)))
                        .Add("total_balance", inner => inner.Sum(sm => sm.Field(x => x.Balance)))))));

        Demo.Check(byState, "按州聚合");
        Demo.Info($"{"州",-6}{"账户数",8}{"平均余额",16}{"余额合计",18}");
        foreach (var bucket in byState.Aggregations!.GetStringTerms("by_state").Buckets)
        {
            var avg = bucket.Aggregations?.GetAverage("avg_balance")?.Value ?? 0;
            var sum = bucket.Aggregations?.GetSum("total_balance")?.Value ?? 0;
            Demo.Info($"  {bucket.Key,-4}{bucket.DocCount,8}{avg,16:N2}{sum,18:N2}");
        }

        Demo.Step("聚合②：按年龄段分组，看各年龄段的余额分布");
        var byAgeRange = await client.SearchAsync<BankAccount>(Index, s => s
            .Size(0)
            .Aggregations(a => a
                .Add("age_ranges", ag => ag.Range(r => r
                    .Field(x => x.Age)
                    .Ranges(
                        new AggregationRange { Key = "20-29", From = 20, To = 30 },
                        new AggregationRange { Key = "30-39", From = 30, To = 40 },
                        new AggregationRange { Key = "40-49", From = 40, To = 50 },
                        new AggregationRange { Key = "50+", From = 50 }))
                    .Aggregations(sub => sub
                        .Add("balance_stats", inner => inner.Stats(st => st.Field(x => x.Balance)))))));

        Demo.Check(byAgeRange, "按年龄段聚合");
        foreach (var bucket in byAgeRange.Aggregations!.GetRange("age_ranges").Buckets)
        {
            var stats = bucket.Aggregations?.GetStats("balance_stats");
            Demo.Info($"  {bucket.Key,-8} {bucket.DocCount,4} 人   平均 {stats?.Avg,12:N2}   最高 {stats?.Max,12:N2}   最低 {stats?.Min,12:N2}");
        }

        Demo.Step("聚合③：嵌套聚合 —— 按州分组，再按性别分组算平均年龄");
        var nested = await client.SearchAsync<BankAccount>(Index, s => s
            .Size(0)
            .Aggregations(a => a
                .Add("by_state", ag => ag.Terms(t => t.Field(x => x.State).Size(3))
                    .Aggregations(sub => sub
                        .Add("by_gender", inner => inner.Terms(t => t.Field(x => x.Gender).Size(2))
                            .Aggregations(deep => deep
                                .Add("avg_age", d => d.Avg(av => av.Field(x => x.Age)))
                                .Add("avg_balance", d => d.Avg(av => av.Field(x => x.Balance)))))))));

        Demo.Check(nested, "嵌套聚合");
        foreach (var stateBucket in nested.Aggregations!.GetStringTerms("by_state").Buckets)
        {
            Demo.Info($"  {stateBucket.Key}（{stateBucket.DocCount} 人）");
            var genderBuckets = stateBucket.Aggregations?.GetStringTerms("by_gender");
            if (genderBuckets is null)
            {
                continue;
            }

            foreach (var genderBucket in genderBuckets.Buckets)
            {
                var avgAge = genderBucket.Aggregations?.GetAverage("avg_age")?.Value ?? 0;
                var avgBalance = genderBucket.Aggregations?.GetAverage("avg_balance")?.Value ?? 0;
                Demo.Info($"      {genderBucket.Key,-8} {genderBucket.DocCount,4} 人   平均年龄 {avgAge,5:0.0}   平均余额 {avgBalance,12:N2}");
            }
        }

        Demo.Step("聚合④：全局统计 + 去重统计");
        var overall = await client.SearchAsync<BankAccount>(Index, s => s
            .Size(0)
            .Aggregations(a => a
                .Add("balance_stats", ag => ag.Stats(st => st.Field(x => x.Balance)))
                .Add("city_count", ag => ag.Cardinality(c => c.Field(x => x.City)))
                .Add("employer_count", ag => ag.Cardinality(c => c.Field(x => x.Employer)))));

        var balance = overall.Aggregations!.GetStats("balance_stats");
        Demo.Info($"余额总览：账户 {balance.Count} 个，平均 {balance.Avg:N2}，最高 {balance.Max:N2}，最低 {balance.Min:N2}，合计 {balance.Sum:N2}");
        Demo.Info($"涉及城市 {overall.Aggregations.GetCardinality("city_count").Value} 个，雇主 {overall.Aggregations.GetCardinality("employer_count").Value} 家");

        // ---------- 5) 组合条件 + 聚合 ----------
        Demo.Step("聚合⑤：先按条件筛选（余额 > 30000）再按城市分组");
        var filtered = await client.SearchAsync<BankAccount>(Index, s => s
            .Size(0)
            .Query(q => q.Range(r => r.Number(n => n.Field(x => x.Balance).Gt(30000))))
            .Aggregations(a => a
                .Add("by_city", ag => ag.Terms(t => t.Field(x => x.City).Size(5))
                    .Aggregations(sub => sub
                        .Add("avg_balance", inner => inner.Avg(av => av.Field(x => x.Balance)))))));

        Demo.Check(filtered, "条件聚合");
        foreach (var bucket in filtered.Aggregations!.GetStringTerms("by_city").Buckets)
        {
            var avg = bucket.Aggregations?.GetAverage("avg_balance")?.Value ?? 0;
            Demo.Info($"  {bucket.Key,-14} {bucket.DocCount,4} 人   平均余额 {avg,12:N2}");
        }
    }

    /// <summary>生成可复现的示例账户数据（用固定种子，每次运行结果一致）。</summary>
    private static List<BankAccount> GenerateAccounts(int count)
    {
        var random = new Random(20240808);
        string[] firstNames = ["Amber", "Hattie", "Nanette", "Dale", "Elinor", "Virginia", "Dillard", "Mcgee", "Aurelia", "Fulton"];
        string[] lastNames = ["Duke", "Bond", "Bates", "Adams", "Ratliff", "Ayala", "Mcdaniel", "Mclean", "Hubbard", "Wilkins"];
        string[] cities = ["Brogan", "Dante", "Nogal", "Yardville", "Veguita", "Nicholson", "Somerset", "Wakulla", "Sunriver", "Chamberino"];
        string[] states = ["IL", "TN", "VA", "CO", "AL", "GA", "OR", "TX", "CA", "NY"];
        string[] employers = ["Pyrami", "Quility", "Netagy", "Zillacom", "Anocha", "Boink", "Comdom", "Freakin"];

        var list = new List<BankAccount>(count);
        for (var i = 0; i < count; i++)
        {
            var first = firstNames[random.Next(firstNames.Length)];
            var last = lastNames[random.Next(lastNames.Length)];
            var cityIndex = random.Next(cities.Length);

            list.Add(new BankAccount
            {
                AccountNumber = 100000 + i,
                FirstName = first,
                LastName = last,
                Age = random.Next(20, 61),
                Gender = random.Next(2) == 0 ? "M" : "F",
                City = cities[cityIndex],
                State = states[cityIndex],
                Balance = Math.Round(random.NextDouble() * 48000 + 2000, 2),
                Employer = employers[random.Next(employers.Length)],
                Email = $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@example.com",
                CreatedAt = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(random.Next(365)),
            });
        }

        return list;
    }
}
