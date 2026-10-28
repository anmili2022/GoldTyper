using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace GoldTyper;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public const int 默认输入间隔最小毫秒 = 500;
    public const int 默认输入间隔最大毫秒 = 5000;
    public const int 默认失败重试次数 = 3;

    public bool 调试日志 = false;
    public bool 按词条长度动态延迟 = true;
    public int 每个字符追加毫秒 = 180;
    public bool 自动再次参加 = false;
    public bool 优先处理读条目标 = true;
    public bool 优先处理近距离目标 = true;
    public string 入口Npc名称 = string.Empty;
    public int 输入间隔最小毫秒 = 默认输入间隔最小毫秒;
    public int 输入间隔最大毫秒 = 默认输入间隔最大毫秒;
    public int 失败重试次数 = 默认失败重试次数;

    public int 发送频道索引 = 0;
    public string 发送内容 = string.Empty;
    public string 点击Addon名称 = "InventoryExpansion";
    public uint 点击节点ID = 7;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface p)
    {
        pluginInterface = p;
        if (Version < 1)
        {
            输入间隔最小毫秒 = 默认输入间隔最小毫秒;
            输入间隔最大毫秒 = 默认输入间隔最大毫秒;
            Version = 1;
            Save();
        }
        if (Version < 2)
        {
            输入间隔最小毫秒 = 默认输入间隔最小毫秒;
            Version = 2;
            Save();
        }
        if (Version < 3)
        {
            优先处理读条目标 = true;
            优先处理近距离目标 = true;
            Version = 3;
            Save();
        }
    }

    public void ResetAutomationDefaults()
    {
        按词条长度动态延迟 = true;
        每个字符追加毫秒 = 180;
        输入间隔最小毫秒 = 默认输入间隔最小毫秒;
        输入间隔最大毫秒 = 默认输入间隔最大毫秒;
        失败重试次数 = 默认失败重试次数;
    }

    public void Save()
    {
        pluginInterface?.SavePluginConfig(this);
    }
}
