using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Nodes;

namespace ElasticSearchDemo.Sections;

/// <summary>
/// 1. 集群与节点信息。对应参考文档里的「集群健康 / 查看集群状态」。
/// 8.x 客户端把这些接口放在命名空间客户端上：client.Cluster、client.Nodes。
/// </summary>
public static class ClusterSection
{
    public static async Task RunAsync(ElasticsearchClient client)
    {
        Demo.Section("集群信息与健康状态");

        // ---- 根接口：版本 / 集群名 / 节点名 ----
        Demo.Step("GET /   （client.InfoAsync）");
        var info = await client.InfoAsync();
        if (!Demo.Check(info, "获取根信息"))
        {
            return;
        }

        Demo.Info($"节点名     : {info.Name}");
        Demo.Info($"集群名     : {info.ClusterName}");
        Demo.Info($"集群 UUID  : {info.ClusterUuid}");
        Demo.Info($"ES 版本    : {info.Version?.Number}（Lucene {info.Version?.LuceneVersion}，构建于 {info.Version?.BuildDate:yyyy-MM-dd}）");

        // ---- 集群健康：旧教程里的 _cluster/health ----
        Demo.Step("GET _cluster/health   （client.Cluster.HealthAsync）");
        var health = await client.Cluster.HealthAsync();
        Demo.Check(health, "获取集群健康");
        Demo.Info($"状态           : {health.Status}  （Green=一切正常 / Yellow=副本未分配 / Red=主分片缺失）");
        Demo.Info($"节点数         : {health.NumberOfNodes}（数据节点 {health.NumberOfDataNodes}）");
        Demo.Info($"活跃分片       : {health.ActiveShards}（主分片 {health.ActivePrimaryShards}，未分配 {health.UnassignedShards}）");
        Demo.Info($"活跃分片百分比 : {health.ActiveShardsPercentAsNumber:0.0}%");

        // ---- 每个索引的健康状况 ----
        if (health.Indices is { Count: > 0 })
        {
            Demo.Info("各索引健康状况：");
            foreach (var (name, indexHealth) in health.Indices)
            {
                Demo.Info($"  - {name,-10} {indexHealth.Status,-7} 分片 {indexHealth.NumberOfShards}，主分片 {indexHealth.ActivePrimaryShards}，副本 {indexHealth.NumberOfReplicas}");
            }
        }
        else
        {
            Demo.Info("当前还没有索引（后面的分节会创建）。");
        }

        // ---- 节点信息：旧教程里的 _cat/nodes ----
        Demo.Step("GET _nodes/os,process,jvm   （client.Nodes.InfoAsync）");
        Demo.Info("注意：8.19 的官方 .NET 客户端没有 client.Cat（没有 CatNamespacedClient），");
        Demo.Info("     要看 _cat 系列的表格输出，直接用浏览器访问 http://localhost:9200/_cat/nodes?v 即可。");

        Metrics metrics = "os,process,jvm";
        var nodes = await client.Nodes.InfoAsync(metrics);
        if (Demo.Check(nodes, "获取节点信息"))
        {
            Demo.Info($"集群名 {nodes.ClusterName}，共 {nodes.Nodes.Count} 个节点");
            foreach (var (id, node) in nodes.Nodes)
            {
                Demo.Info($"  - {node.Name}  (id {id[..Math.Min(10, id.Length)]}…)");
                Demo.Info($"      版本 {node.Version}，角色 {string.Join("/", node.Roles ?? [])}");
                Demo.Info($"      操作系统 {node.Os?.Name} {node.Os?.Version}（{node.Os?.Arch}），可用处理器 {node.Os?.AvailableProcessors}");
                Demo.Info($"      JVM {node.Jvm?.VmName} {node.Jvm?.Version}，堆上限 {node.Jvm?.Mem?.HeapMaxInBytes / 1024 / 1024} MB");
                Demo.Info($"      物理内存 {node.Os?.Mem?.TotalInBytes / 1024 / 1024} MB，进程 PID {node.Process?.Id}");
            }
        }
    }
}
