# Elasticsearch 8.x + .NET 学习 Demo

一个可以**直接跑起来**的 Elasticsearch 入门 demo，覆盖从集群健康、索引管理、文档 CRUD、批量操作、检索（Query DSL）到聚合分析的完整链路。

参考的入门文档是 [Elasticsearch 快速开始](https://www.cnblogs.com/cjsblog/p/9439331.html)，但那份文档用的 ES 版本较老（NEST 客户端 / ES 5.x~6.x 语法）。**本项目全部改用最新的 8.x**：

| | 旧教程（参考文档） | 本项目 |
|---|---|---|
| Elasticsearch | 5.x / 6.x | **8.19.4** |
| .NET 客户端 | NEST | **Elastic.Clients.Elasticsearch 8.19.27** |
| .NET | .NET Framework / Core 2.x | **.NET 9** |
| 安全 | 默认无认证 | demo 关闭认证，README 说明生产配置 |

> 官方从 8.x 起用 `Elastic.Clients.Elasticsearch` 取代 NEST，API 风格、类型体系、命名都变了，**旧教程里的 C# 代码基本不能直接编译**。这个项目就是按新客户端的实际行为写的，所有代码都已在真实 ES 8.19.4 上跑通。

---

## 一、快速开始

### 1. 启动 Elasticsearch

需要先装好 Docker Desktop。

```bash
cd elasticsearch_demo
docker compose up -d
```

首次会拉取 `elasticsearch:8.19.4` 镜像（约 1GB，耐心等）。等健康检查通过后，验证一下：

```bash
curl http://localhost:9200
curl http://localhost:9200/_cluster/health
```

看到 `"number" : "8.19.4"` 和 `"status" : "green"` 就 OK 了。

想同时用 Kibana 看数据、敲 DSL（强烈建议，对照 demo 看很直观）：

```bash
docker compose --profile kibana up -d
# 首次启动约 30~60 秒，看到 "Kibana is now available" 后打开 http://localhost:5601
```

Kibana 在 compose 里是用 `profiles` 隔离的，不加 `--profile kibana` 就只启动 ES，不会多占内存。具体怎么用见第五节。

### 2. 运行 demo

```bash
dotnet run --project src/ElasticSearchDemo
```

程序会等 ES 就绪（最多 60 秒），然后依次跑 8 个分节。全部跑完大约十几秒。

只想看某几个分节，把 key 当参数传进去：

```bash
dotnet run --project src/ElasticSearchDemo -- index documents search
```

可用的 key：

| key | 内容 |
|---|---|
| `cluster` | 集群信息与健康状态 |
| `index` | 索引管理（创建 / 查看 / 设置 / 统计 / 删除） |
| `documents` | 文档 CRUD（增 / 查 / 改 / 删） |
| `bulk` | 批量操作（Bulk / BulkAll） |
| `search` | 检索（Query DSL） |
| `aggregations` | 聚合分析 |
| `advanced` | 分词 / 按查询删改 / 别名 / ES\|QL |
| `bank` | 完整示例：bank 账户数据 |

ES 地址默认 `http://localhost:9200`，可以用环境变量覆盖：

```bash
set ES_URL=http://192.168.1.10:9200
dotnet run --project src/ElasticSearchDemo
```

demo 是**可重复运行**的：`index` 分节会先删掉残留的 `books` 索引再重建，`bank` 分节同理，所以随便反复跑。

---

## 二、项目结构

```
elasticsearch_demo/
├─ docker-compose.yml            ES 8.19.4 单节点 + 可选 Kibana
├─ README.md
└─ src/ElasticSearchDemo/
   ├─ ElasticSearchDemo.csproj
   ├─ Program.cs                 入口：连接设置、等待就绪、分节调度
   ├─ Models.cs                  Book / BankAccount 两个 POCO
   ├─ Demo.cs                    控制台输出、原始 JSON 打印、响应检查、等待集群
   └─ Sections/
      ├─ ClusterSection.cs       1. 集群信息与健康
      ├─ IndexSection.cs         2. 索引管理
      ├─ DocumentSection.cs      3. 文档 CRUD
      ├─ BulkSection.cs          4. 批量操作
      ├─ SearchSection.cs        5. 检索（Query DSL）
      ├─ AggregationSection.cs   6. 聚合分析
      ├─ AdvancedSection.cs      7. 分词 / 删改 / 别名 / ES|QL
      └─ BankSection.cs          8. bank 完整示例
```

---

## 三、各分节内容

### 1. 集群信息与健康（`cluster`）

- `client.InfoAsync()` —— 版本、集群名、节点名
- `client.Cluster.HealthAsync()` —— Green / Yellow / Red，分片数，逐索引健康状态
- `client.Nodes.InfoAsync(metrics)` —— 节点 OS / JVM / 进程信息

> ⚠️ **8.19 的官方 .NET 客户端没有 `client.Cat`**（不存在 `CatNamespacedClient`）。要看 `_cat` 系列表格，直接 `curl http://localhost:9200/_cat/nodes?v`，或者用 Kibana。

### 2. 索引管理（`index`）

- `Indices.CreateAsync<Book>(name, descriptor)` —— settings + mappings + 自定义分词器
- 自定义 `book_analyzer`（standard + lowercase + asciifolding）、`chinese_analyzer`（cjk_bigram 双字词）
- `GetMappingAsync` / `GetAsync` / `ExistsAsync` / `PutSettingsAsync`
- `CloseAsync` / `OpenAsync` / `RefreshAsync` / `StatsAsync`

### 3. 文档 CRUD（`documents`）

- 新增（指定 id / 自动 id）、按 id 查、判断存在
- 局部更新（`UpdateAsync<TDoc, TPartial>`）、painless 脚本更新、`doc_as_upsert`
- 删除、重复删除（`NotFound` 也是正常响应）

### 4. 批量操作（`bulk`）

- `BulkAsync` + `IndexMany` —— 一次请求写多条
- 一条请求里混合 index / update / delete
- 逐个检查 `ResponseItem`（**整体 200 不代表每条都成功**）
- `BulkAll` + `Wait(...)` —— 自动分批、重试、退避，适合几万条以上的导入

### 5. 检索（`search`）

- `match_all`、`from`/`size` 分页、`sort` 排序、`TrackTotalHits`
- `_source` 过滤（两种写法，见下文「坑」）
- `match`（含 `operator: and/or`）、`multi_match`、`term`、`terms`、`range`（数字 / 日期）
- `bool`：`must` / `filter` / `should` / `must_not` 组合
- `highlight` 高亮、`CountAsync` 只数数

### 6. 聚合分析（`aggregations`）

- `terms` 分组计数、`terms` + `avg`/`max`/`min`/`sum` 子聚合
- `range` 区间分桶 + 子聚合
- `stats` / `cardinality` / `value_count`
- 带 `query` 的聚合、`filter` 聚合、`global` 聚合
- `date_histogram` 按年分桶（用方块图打印）
- 数组字段（`tags`）的 `terms` 聚合

### 7. 进阶（`advanced`）

- `_analyze` 看分词结果（8 种对比：自定义 / standard / english / asciifolding / 中文 bigram / keyword / 临时过滤器 / 指定字段）
- `_update_by_query` 按查询批量更新（打 9 折）
- `_delete_by_query` 按查询批量删除
- 索引别名（alias）的添加、通过别名检索、移除
- **ES\|QL**（8.11+ 新特性）：管道式查询语言，以及强类型映射 `QueryAsObjectsAsync`

### 8. bank 完整示例（`bank`）

用官方示例数据集走一遍完整流程：建索引 → 造 200 条数据 → 条件检索 → 5 组聚合（按州 / 按年龄段 / 嵌套 / 全局统计 / 条件聚合）。数据用固定随机种子生成，每次运行结果一致。

---

## 四、8.x 客户端与旧教程的关键差异

这一节是踩坑记录，**照旧教程写 C# 会遇到的坑基本都在这里**。

### 1. 命名策略：默认 camelCase

旧教程（NEST）默认把 `PublishDate` 序列化成 `publishDate`，而 ES 里手写的 mapping 通常是 `publish_date`，两边就对不上了。

本项目统一在 POCO 上用 `[JsonPropertyName("publish_date")]` 显式声明为 snake_case。好处是**文档序列化和查询字段推断用的是同一个名字**，不会出现「写入时是 `publishDate`、查询时查 `publish_date`」的错位。

> 注意 `DefaultFieldNameInferrer(...)` 只影响字段名推断，**不影响文档序列化**，别指望用它统一两边。

### 2. Descriptor 必须显式转成 Request 才会序列化

Fluent 写法（`SearchRequestDescriptor<T>`）要序列化时，必须显式转：

```csharp
var d = new SearchRequestDescriptor<Book>().Query(q => q.MatchAll());
SearchRequest r = d;                       // 不转的话序列化结果是 {}
```

而直接调用 `client.SearchAsync<Book>(...)` 不需要关心这个，客户端内部会处理。

### 3. 泛型重载的索引名怎么传（三种不同的坑）

**泛型**方法（`XxxAsync<TDocument>`）基本没有「只传索引名」的重载，规则是：

```csharp
// ✗ 编译不过（CS1503: 无法从 string 转换为 CancellationToken）
await client.CountAsync<Book>(Index);
await client.Indices.GetMappingAsync<Book>(Index);

// ✓ 索引写进 Descriptor
await client.CountAsync<Book>(c => c.Indices(Index));
await client.Indices.GetMappingAsync<Book>(m => m.Indices(Index));

// ✓ 或者用非泛型版本
await client.CountAsync(Index);
```

注意 `DeleteByQuery` / `UpdateByQuery` 是个例外，**它们有 `(Indices, Action<...>)` 重载**，所以下面两种写法都行：

```csharp
await client.DeleteByQueryAsync<Book>(Index, d => d.Query(...).Refresh(true));
await client.DeleteByQueryAsync<Book>(d => d.Indices(Index).Query(...).Refresh(true));
```

而 `IndexAsync` 更阴险 —— 下面这行**能编译**，但运行时报
`ArgumentException: Index name is null for the given type...`：

```csharp
await client.IndexAsync(book, Index);   // ✗ 字符串被当成了「文档 id」！
```

因为它匹配到的是 `IndexAsync<TDocument>(TDocument document, Id id)` 这个重载 —— 索引名被当成了文档 id，而索引本身没地方传。自动生成 id 的正确写法：

```csharp
await client.IndexAsync(book, i => i.Index(Index));   // ✓
```

另一种通用办法是给类型配默认索引，配了之后上面这些都能省掉索引参数：

```csharp
settings.DefaultMappingFor<Book>(m => m.IndexName("books"));
```

### 4. `_source` 过滤：两种写法走的是不同通道

```csharp
// ① SourceIncludes / SourceExcludes —— 走 URL 查询串
.SourceIncludes(b => b.Title, b => b.Price)
// 实际请求：GET /books/_search?_source_includes=title%2Cprice

// ② Source(sf => sf.Filter(...)) —— 走请求体
.Source(sf => sf.Filter(f => f.Includes(b => b.Title).Excludes(b => b.Summary)))
// 实际请求体：{"_source": {"includes": "title", "excludes": "summary"}}
```

两种都生效（实测返回的文档确实只剩 `title` / `price`）。但 ① 的过滤条件**不在请求体里**，如果只看请求体调试，会误以为没生效 —— 记得也看一眼 URL。

### 5. 响应不抛异常，要看 `IsValidResponse`

8.x 客户端默认不因为 4xx/5xx 抛异常（除非是传输层错误）。所以每个响应都要检查：

```csharp
if (!resp.IsValidResponse)
    Console.WriteLine(resp.ElasticsearchServerError?.Error?.Reason);
```

另外 **404 在很多场景下是「正常结果」而不是错误**：文档不存在、索引不存在，`IsValidResponse` 依然是 `true`，靠 `Found` / `Exists` 属性判断。`Demo.Check(...)` 就是干这个的。

### 6. `settings.analysis` 反序列化不完整

`client.Indices.GetAsync(...)` 拿到的 `IndexSettings.Analysis` 可能是 `null`，哪怕 ES 里确实配了分词器 —— 客户端没有完整映射这层嵌套结构。

**要看原文就直读响应体**（demo 里 `Demo.RawBody(resp)` 就是干这个的）：

```csharp
var json = System.Text.Encoding.UTF8.GetString(resp.ApiCallDetails!.ResponseBodyInBytes!);
```

### 7. 分词器的常见误解

- `lowercase` **只转小写，不做词干还原**：`foxes` 还是 `foxes`，不会变成 `fox`。想要词干还原要用 `porter_stem` 过滤器或内置 `english` 分词器（`english` 还会去掉停用词）。
- `asciifolding` 负责去音标：`café` → `cafe`，这样搜 `cafe` 也能命中。
- `standard` 分词器**对中文是逐字切分**，检索效果很差。要么用 `cjk_bigram` 凑合（相邻两字组词），要么装 `analysis-ik` 插件做词典分词。
- 检索结果不对时，先 `_analyze` 看一眼词条，比盯着 DSL 猜快得多。

### 8. `Metrics` 参数有二义性

`client.Nodes.InfoAsync("os,process,jvm")` 这种字面量写法会编译报 CS0121（`NodeIds?` 和 `Metrics?` 两个重载都能匹配）。声明成带类型的变量即可：

```csharp
Metrics metrics = "os,process,jvm";
await client.Nodes.InfoAsync(metrics);
```

### 9. ES|QL（8.11+）

新的管道式查询语言，语法像 SPL / KQL：

```
FROM books | STATS count = COUNT(*), avg_price = AVG(price) BY category | SORT count DESC
```

`client.Esql.QueryAsync(...)` 返回的是 `ElasticsearchStreamResponse`，`Body` 是 `Stream`，要自己读；返回结构是「列 + 行」的表格，不是 `_search` 那种 JSON。也有强类型映射 `QueryAsObjectsAsync<T>`（该 API 已标记过时，官方建议用 LINQ 风格的 `CreateQuery<T>()`，demo 两种都演示了）。

---

## 五、Kibana 看板：在界面里看 demo 的数据

Kibana 是 ES 官方配套的可视化 / 管理界面（就是常说的「看板」）。本节内容都在 `kibana:8.19.4` 上实测过。

```bash
docker compose --profile kibana up -d
# 等 30~60 秒，然后打开 http://localhost:5601
```

验证 Kibana 本身是否就绪（看到 `"level":"available"` 即可）：

```bash
curl http://localhost:5601/api/status
```

### 1. 先建 Data View（必须先做，否则 Discover 里看不到任何东西）

Kibana 不像 `curl` 那样直接打索引名，它需要先给索引注册一个 **Data View**（旧版叫 Index Pattern），告诉 Kibana「有哪些索引、用哪个字段当时间轴」。

**注意：Data View 是存在 Kibana 自己的存储里的，仓库里没法带**。所以从 GitHub 克隆下来后，需要自己建一次：

1. 打开 http://localhost:5601
2. 左侧菜单 → **Management** → **Data Views**（直达链接 http://localhost:5601/app/management/kibana/dataViews）
3. **Create data view**，填两项：
   - **Name** 填 `books`，**Index pattern** 也填 `books`
   - **Timestamp field** 选 `publish_date`
4. 保存。再重复一次，建第二个：Name / Index pattern 都填 `bank`，**Timestamp field** 选 `created_at`

| Data View | 索引 | 时间字段 | 数据量 |
|---|---|---|---|
| `books` | books | `publish_date` | 9 条 |
| `bank` | bank | `created_at` | 200 条 |

> `publish_date` / `created_at` 能出现在下拉框里，说明 ES 里这两个字段是 `date` 类型 —— 也就是 `IndexSection.cs` / `BankSection.cs` 里 `.Date(b => b.PublishDate)` 这类映射生效了。

### 2. Discover：逐条浏览文档

打开 http://localhost:5601/app/discover → 左上角 **Data view** 选 `books（图书）`。

> ⚠️ **这一步最容易卡住：右上角时间范围默认是「最近 15 分钟」，而 demo 数据是 1999~2026 年的，不改时间范围会显示 0 条，让人以为数据没写进去。**
>
> 点右上角时间选择器 → **Absolute**，填一个宽范围，比如 `1999-01-01` ~ `2030-01-01`，就能看到 9 条文档（含中文标题「Elasticsearch 权威指南」）。
>
> 实测范围：`books.publish_date` 是 **1999-10-20 ~ 2026-10-20**，`bank.created_at` 是 **2024-01-03 ~ 2024-12-29**。时间范围没覆盖到，就一条都看不到。

在 Discover 里还能直接：
- 点左侧字段列表里的 `+` 把字段加进表格列（比如 `category` / `price` / `tags`）
- 用 KQL 语法在搜索框过滤，例如 `category : "search-engine"`、`price > 45`
- 点某行的展开箭头，看这条文档完整的 `_source` JSON

### 3. Dev Tools：直接粘贴 demo 里的 DSL

http://localhost:5601/app/dev_tools#/console —— 左边写请求，按 `Ctrl+Enter` 执行，右边出结果。这是对照 C# 代码看 DSL 最快的方式。

```
GET /books/_search
{
  "size": 0,
  "aggs": {
    "by_category": {
      "terms": { "field": "category" },
      "aggs": { "avg_price": { "avg": { "field": "price" } } }
    }
  }
}
```

这条和 `AggregationSection.cs` 里的 terms + avg 子聚合是等价的。想看分词效果也可以直接敲：

```
POST /books/_analyze
{ "analyzer": "chinese_analyzer", "text": "Elasticsearch 权威指南" }
```

### 4. Lens / Dashboard：画图

- **Lens**（http://localhost:5601/app/lens）—— 拖字段出图。左边选 Data view，把 `price` 拖到纵轴、`category` 拖到横轴就是柱状图。**同样记得先改时间范围**，Lens 默认也是「最近 15 分钟」。
- **Dashboard** —— 把上面存下来的图拼成一块看板。

### 5. 关于 demo 数据会被覆盖

`dotnet run --project ...` 每次执行 `index` / `bank` 分节都会**删掉并重建同名索引**，所以 Discover 里看到的数据就是最近一次运行的结果，字段结构不变。

另外 `docker compose down -v` 会连 Kibana 的存储一起删掉，**上面建的 Data View 也会丢**，需要重新建一次（`docker compose down` 不带 `-v` 则保留）。

---

## 六、生产环境注意事项

docker-compose 里为了 demo 方便，**关闭了安全认证**（`xpack.security.enabled=false`），只适合本机学习。生产环境必须：

1. **开启认证**，用内置用户或创建专用账号，别用 `elastic` 超级用户；
2. **启用 HTTPS**，客户端用证书指纹校验：

```csharp
var settings = new ElasticsearchClientSettings(new Uri("https://es.example.com:9200"))
    .CertificateFingerprint("<从 ES 启动日志里拿到的 SHA-256 指纹>")
    .Authentication(new BasicAuthentication("demo-user", "<密码>"));
```

3. **不要给 9200 端口配公网映射**，只在内网 / VPC 内暴露；
4. 副本数按节点数调整（单节点时副本给 0，多节点至少 1），demo 里是 `NumberOfReplicas(0)`；
5. 分片数别照抄 demo 的 `1`，按数据量和节点数规划；
6. 生产写入别用 `Refresh.WaitFor` / `Refresh(true)`，那是 demo 为了让结果立即可见才加的，会严重影响写入性能。

---

## 七、清理

```bash
docker compose down        # 停止容器，数据保留在 es-data 卷
docker compose down -v     # 停止并删除数据（Kibana 的 Data View 也会一起丢）
```
