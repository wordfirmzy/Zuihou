using UnityEngine;

public enum GameMode { Bot, Host, Client }

public static class RuntimeConfig
{
    public static GameMode Mode = GameMode.Bot;
    public static string Address = "127.0.0.1";
    public static ushort Port = 7777;
}
