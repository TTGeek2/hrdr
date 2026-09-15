using System.Text.Json.Serialization;

namespace Hrdr.Core.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WorkItemType
{
    Feature = 0,
    Bug = 1
}
