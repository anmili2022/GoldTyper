using System;
using ECommons.Automation;

namespace GoldTyper.Game;

public static class ChatSender
{
    public static readonly string[] ChannelLabels =
    {
        "说话 (/s)",
        "喊话 (/y)",
        "喊叫 (/sh)",
        "小队 (/p)",
        "团队 (/ra)",
        "部队 (/fc)",
    };

    public static readonly string[] ChannelCommands = { "s", "y", "sh", "p", "ra", "fc" };

    public static bool Send(int channelIndex, string text, out string message)
    {
        try
        {
            if (channelIndex < 0 || channelIndex >= ChannelCommands.Length)
            {
                message = "无效频道";
                return false;
            }

            var content = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (content.Length == 0)
            {
                message = "发送内容为空";
                return false;
            }

            var command = ChannelCommands[channelIndex];
            Chat.SendMessage($"/{command} {content}");
            message = $"已发送 /{command}: {content}";
            return true;
        }
        catch (Exception ex)
        {
            message = $"发送失败: {ex.Message}";
            return false;
        }
    }
}
