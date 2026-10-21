using System.Text.Json.Serialization;

namespace ElasticSearchDemo;

/// <summary>
/// 图书文档。对应索引 books。
/// 注意：8.x 客户端的默认字段名推断是 camelCase（属性 <c>PublishDate</c> -> <c>publishDate</c>），
/// 这里统一用 <see cref="JsonPropertyNameAttribute"/> 显式声明为 snake_case，
/// 这样“写文档”和“构造查询条件”用的字段名完全一致，避免两边对不上。
/// </summary>
public sealed class Book
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    /// <summary>摘要，用 ik/标准分词器切成词条，用于全文检索。</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>分类。keyword 类型，用于精确匹配、排序和 terms 聚合。</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    /// <summary>标签，keyword 数组。</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("price")]
    public double Price { get; set; }

    [JsonPropertyName("stock")]
    public int Stock { get; set; }

    [JsonPropertyName("publish_date")]
    public DateTimeOffset PublishDate { get; set; }

    [JsonPropertyName("in_stock")]
    public bool InStock { get; set; }
}

/// <summary>
/// 银行账户文档，对应索引 bank。
/// 这个数据结构取自 Elasticsearch 官方示例数据集，参考文档里的聚合例子也是用它。
/// </summary>
public sealed class BankAccount
{
    [JsonPropertyName("account_number")]
    public int AccountNumber { get; set; }

    [JsonPropertyName("firstname")]
    public string FirstName { get; set; } = string.Empty;

    [JsonPropertyName("lastname")]
    public string LastName { get; set; } = string.Empty;

    [JsonPropertyName("age")]
    public int Age { get; set; }

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("balance")]
    public double Balance { get; set; }

    [JsonPropertyName("employer")]
    public string Employer { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
