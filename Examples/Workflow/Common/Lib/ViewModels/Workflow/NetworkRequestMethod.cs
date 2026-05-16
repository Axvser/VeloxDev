using VeloxDev.AI;

namespace Demo.ViewModels;

[AgentContext(AgentLanguages.Chinese, "HTTP 请求方法枚举，用于 EnumSelectorNodeViewModel 的 OutputSlots 路由")]
[AgentContext(AgentLanguages.English, "HTTP request method enum used to drive output slot routing in EnumSelectorNodeViewModel.")]
public enum NetworkRequestMethod
{
    [AgentContext(AgentLanguages.Chinese, "HTTP GET 请求")]
    [AgentContext(AgentLanguages.English, "HTTP GET request — read/query resource.")]
    Get,

    [AgentContext(AgentLanguages.Chinese, "HTTP POST 请求")]
    [AgentContext(AgentLanguages.English, "HTTP POST request — create resource.")]
    Post,

    [AgentContext(AgentLanguages.Chinese, "HTTP PUT 请求")]
    [AgentContext(AgentLanguages.English, "HTTP PUT request — replace resource.")]
    Put,

    [AgentContext(AgentLanguages.Chinese, "HTTP PATCH 请求")]
    [AgentContext(AgentLanguages.English, "HTTP PATCH request — partially update resource.")]
    Patch,

    [AgentContext(AgentLanguages.Chinese, "HTTP DELETE 请求")]
    [AgentContext(AgentLanguages.English, "HTTP DELETE request — remove resource.")]
    Delete,
}
