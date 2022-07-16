using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.GamePad;
using Dalamud.Game.ClientState.Party;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.Game.Text;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using Dalamud.Plugin;
using XivCommon;
using System.Text.RegularExpressions;

using GamepadTweaks.Attributes;

namespace GamepadTweaks
{
    public class Plugin : IDalamudPlugin
    {
        [PluginService] public static DalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] public static ChatGui Chat { get; private set; } = null!;
        [PluginService] public static ClientState ClientState { get; private set; } = null!;
        [PluginService] public static GameGui GameGui { get; private set; } = null!;
        [PluginService] public static ObjectTable Objects { get; private set; } = null!;
        [PluginService] public static SigScanner SigScanner { get; private set; } = null!;
        [PluginService] public static PartyList PartyList { get; private set; } = null!;
        [PluginService] public static GamepadState GamepadState { get; private set; } = null!;
        [PluginService] public static TargetManager TargetManager { get; private set; } = null!;
        [PluginService] public static Framework Framework { get; private set; } = null!;

        public static XivCommonBase XivCommon { get; private set; } = null!;
        public static Configuration Config { get; private set; } = null!;
        public static PluginCommandManager<Plugin> Commands { get; private set; } = null!;

        public string Name => "Gamepad Tweaks (for Healers)";
        public ActionMap Actions = new ActionMap();

        private PluginWindow Window { get; set; }
        private WindowSystem WindowSystem { get; set; }

        private GamepadActionManager GamepadActionManager;

        public Plugin(
            DalamudPluginInterface pi,
            CommandManager commands)
        {
            XivCommon = new XivCommonBase();
            Config = Configuration.Load();

            // Load all of our commands
            Commands = new PluginCommandManager<Plugin>(this, commands);
            GamepadActionManager = new GamepadActionManager();

            // Initialize the UI
            Window = new PluginWindow();
            WindowSystem = new WindowSystem(Name);
            WindowSystem.AddWindow(Window);

            PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
            Framework.Update += GamepadActionManager.UpdateFramework;
        }

        [Command("/gt")]
        [HelpMessage(@"Open setting panel.
/gt on/off → Enable/Disable this plugin.
/gt info → Show gt info.
/gt add <action> [<selectOrder>] → Add specific <action> in monitor.
/gt remove <action> → Remove specific monitored <action>.
/gt reset [<action>] → Reset combo index for given group.

<action>        Action name (in string).
<selectOrder>   The order for party member selection (only accepted Dpad and y/b/a/x buttons (Xbox)).")]
        public void CommandGi(string command, string args)
        {
            if (args is null || args == "") {
                Window.Toggle();
            } else {
                // add "Celestial Intersection" 'y b a x'
                // add Haima default
                var argv = args.Trim().Split(" ", 2).Where(a => a != "").ToList();
                switch(argv[0])
                {
                    case "on":
                        Echo("[GamepadTweaks] Enabled.");
                        GamepadActionManager.Enable();
                        Chat.UpdateQueue();
                        break;
                    case "off":
                        Echo("[GamepadTweaks] Disabled.");
                        GamepadActionManager.Disable();
                        Chat.UpdateQueue();
                        break;
                    case "info":
                        string bs(bool x) => x ? "●" : "○";
                        // string pr<T>(T x, int s = 6) => $"{x}".PadRight(s);
                        // string pl<T>(T x, int s = 6) => $"{x}".PadLeft(s);
                        Echo("====== [S GamepadTweaks] ======");
                        Echo($"小队成员: {bs(Config.alwaysInParty || PartyList.Length > 0)}");
                        Echo($"自动锁定: {bs(Config.autoTargeting)}");
                        foreach(string a in Config.gtoff) {
                            Echo($"[G] {a}");
                        }
                        foreach(string a in Config.gs) {
                            Echo($"[D] {a}");
                        }
                        foreach(var a in Config.rules) {
                            Echo(@$"[U] {a.Key} =>
        {a.Value}");
                        }
                        Echo("====== [E GamepadTweaks] ======");
                        Chat.UpdateQueue();
                        break;
                    case "add":
                        try {
                            var actionkv = argv[1].Trim();
                            var pattern = new Regex(@"[\""\']?\s*(?<action>[\w\s]+\w)\s*[\""\']?(\s+[\""\']?\s*(?<order>[\w\s]+\w)\s*[\""\']?)?",
                                                    RegexOptions.Compiled);

                            var match = pattern.Match(actionkv);

                            var action = match.Groups.ContainsKey("action") ? match.Groups["action"].ToString() : "";
                            var order = match.Groups.ContainsKey("order") ? match.Groups["order"].ToString() : "";

                            if (order != "") {
                                Config.rules.TryAdd(action, order);
                            } else {
                                if (!Config.gs.Contains(action)) {
                                    Config.gs.Add(action);
                                }
                            }
                            Echo($"Add action: {action} ... [ok]");
                        } catch(Exception e) {
                            Chat.PrintError($"Add action failed.");
                            PluginLog.Error($"Exception: {e}");
                        }
                        Chat.UpdateQueue();
                        break;
                    case "remove":
                        try {
                            var action = argv[1];
                            Config.gs.Remove(action);
                            Config.rules.Remove(action);
                            Echo($"Remove action: {action} ... [ok]");
                        } catch(Exception e) {
                            Chat.PrintError($"Remove action failed.");
                            PluginLog.Error($"Exception: {e}");
                        }
                        Chat.UpdateQueue();
                        break;
                    case "reset":
                        uint groupID = 0;
                        if (argv.Count > 1) {
                            groupID = Actions[argv[1].Trim()];
                        }
                        Config.ResetComboState(groupID);
                        return;
                    default:
                        break;
                }

                try {
                    Config.Update();
                } catch(Exception e) {
                    PluginLog.Error($"Exception: {e}");
                }
            }
        }

        public static bool Ready => ClientState.LocalPlayer is not null;
        public static PlayerCharacter? Player => ClientState.LocalPlayer;

        public static void Echo(string s)
        {
            Chat.PrintChat(new XivChatEntry() {
                Message = s,
                Type = XivChatType.Debug,
            });
        }

        public static void Send(string s)
        {
            PluginLog.Debug($"[Send] {s}");
            XivCommon.Functions.Chat.SendMessage(s);
        }

        public void Error(string s) {
            Chat.PrintError(s);
        }

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            Commands.Dispose();

            Framework.Update -= GamepadActionManager.UpdateFramework;
            GamepadActionManager.Dispose();

            Config.Save();

            PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
            WindowSystem.RemoveAllWindows();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}