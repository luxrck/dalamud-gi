using Dalamud.Game.ClientState.Objects;
using Dalamud.Logging;


namespace GamepadTweaks
{
    public enum ComboType : int
    {
        // m : 触发即跳转到下一个, 不进行任何确认
        Manual = 0,
        // l : 如果Action位于combo-chain中, 触发Action则会将该combo-chain的当前状态指向该action
        // a1 *a2 a3 a4 a5 : Pre : Trigger a4
        // a1 a2 a3 *a4 a5 : Now
        Linear = 1,
        // s : 和l类似, 不过严格按照combo-chain的顺序从头到尾.
        // a1 *a2 a3 a4 a5 : Pre : Trigger a3
        // a1 *a2 a3 a4 a5 : Now : Trigger a2
        // a1 a2 *a3 a4 a5 : Nxt
        Strict = 2,
        // ls : 和l类似, 不过可以自动跳过处于cd中的能力技
        // a1 *a2 b1 a4 a5 : Pre : Trigger a2
        // a1 a2 *b1 a4 a5 : Now : if b1 is avaliable
        // a1 a2 b1 *a4 a5 : Now : if b1 in CD
        LinearBlocked = 3,
        StrictBlocked = 4,
        Ochain = 6,
        // a : macro style
        Async = 7,
    }

    // Used by Ochain
    public enum ComboActionType : int
    {
        // <action>
        Single = 0x01,
        // <action> {m,u}
        Multi = 0x02,
        // <action> *
        Skipable = 0x04,
        // <action> !
        Blocking = 0x08,
        Ability = 0x10,
        // <action> ?
        SingleSkipable = Single | Skipable,
        // <action> {a,b}?
        MultiSkipable = Multi | Skipable,
    }

    public class ComboAction
    {
        public uint ID = 0;
        public ComboActionType Type = ComboActionType.Single;
        public int MinimumCount = 1;
        public int MaximumCount = 1;
        public int Count = 0;
        public bool Executed = false;
        public DateTime LastTime = DateTime.Now;

        public Actions Actions = new Actions();

        public void Restore() { Count = 0; Executed = false; }

        public void Update()
        {
            Executed = true;
            Count += 1;
        }

        // public bool CanSkip()
        // {
        //     var recast = Actions.RecastTimeRemain(ID);
        // }
    }

    public class ComboGroup
    {
        public uint GroupID;
        public int CurrentIndex;
        public List<ComboAction> ComboActions;
        public ComboType Type;

        public Actions Actions = new Actions();
        public DateTime LastTime = DateTime.Now;

        private TargetManager TargetManager = Plugin.TargetManager;

        private SemaphoreSlim actionLock = new SemaphoreSlim(1, 1);
        private SemaphoreSlim actionLockHighPriority = new SemaphoreSlim(1, 1);

        public ComboGroup(uint id, List<ComboAction> actions, string ctype = "l")
        {
            GroupID = id;
            CurrentIndex = 0;
            switch (ctype) {
                case "m":
                    Type = ComboType.Manual; break;
                case "l":
                    Type = ComboType.Linear; break;
                case "s":
                    Type = ComboType.Strict; break;
                case "lb":
                    Type = ComboType.LinearBlocked; break;
                case "sb":
                    Type = ComboType.StrictBlocked; break;
                case "o":
                    Type = ComboType.Ochain; break;
                case "a":
                    Type = ComboType.Async; break;
                default:
                    Type = ComboType.Linear; break;
            }
            ComboActions = actions;
        }

        public uint Current(uint lastComboAction = 0, float comboTimer = 0f)
        {
            return ComboActions[CurrentIndex].ID;
        }

        public bool Contains(uint actionID) => ComboActions.Any(x => Actions.Equals(x.ID, actionID));

        public void StateReset() => CurrentIndex = 0;

        public async Task<bool> StateUpdate(uint actionID, ActionStatus status = ActionStatus.Ready, bool succeed = true, DateTime now = default(DateTime))
        {
            if (!Plugin.Ready) return false;
            if (status == ActionStatus.Locking) return false;

            var index = ComboActions.FindIndex(CurrentIndex, x => Actions.Equals(x.ID, actionID));
            if (index == -1)
                index = ComboActions.FindIndex(0, x => Actions.Equals(x.ID, actionID));

            var originalIndex = index;

            if (index == -1) index = CurrentIndex;

            var caction = ComboActions[index % ComboActions.Count];
            var cadjust = Actions.AdjustedActionID(caction.ID);
            var cstatus = originalIndex == -1 ? Actions.ActionStatus(cadjust) : status;
            var crgroup = Actions.RecastGroup(cadjust);
            var cremain = Actions.RecastTimeRemain(cadjust);

            if (cstatus == ActionStatus.NotLearned) {
                CurrentIndex = (index + 1) % ComboActions.Count;
                return true;
            }

            if (status != ActionStatus.Ready && status != ActionStatus.Pending && status != ActionStatus.NotSatisfied)
                return true;

            int animationDelay = 400;

            var baseActionID = Actions.BaseActionID(actionID);

            // *a1 -> a2 -> a3 -> a1 -> a4
            // task1 : Update(a1 Ready) -> Lock -> DoSomething -> Wait(500ms) -> CurrentIndex++ -> Unlock
            // task2 :      |-> Update(a1 Pending) -> Give up if not waited -> ...
            // 0    1    2    3    4    5    6    7    8    9    10
            // a         b
            //      a
            //                a
            if (index == -1) {
                if (Type != ComboType.Ochain) {
                    return false;
                } else {
                    if (!(await this.actionLock.WaitAsync(0))) return false;
                    if (!(await this.actionLockHighPriority.WaitAsync(0))) {
                        this.actionLock.Release();
                        return false;
                    }
                }
            } else {
                await this.actionLock.WaitAsync();
                // if ((now - this.LastTime).TotalMilliseconds < animationTotal) {
                if (now < this.LastTime) {
                    // 这可以确保在小于animationDelay的时间段内发出的pending actions可以被忽略
                    if (status == ActionStatus.Pending) {
                        this.actionLock.Release();
                        return false;
                    } else if (status != ActionStatus.Ready) {
                        if (!(await this.actionLockHighPriority.WaitAsync(0))) {
                            this.actionLock.Release();
                            return false;
                        }
                    } else {
                        await this.actionLockHighPriority.WaitAsync();
                    }
                } else {
                    await this.actionLockHighPriority.WaitAsync();
                }
            }

            switch (Type)
            {
                case ComboType.Manual:
                    index = (index + 1) % ComboActions.Count;
                    break;
                case ComboType.Strict:
                    if (!Actions.Equals(ComboActions[CurrentIndex].ID, actionID))
                        break;
                    if ((status == ActionStatus.Ready || status == ActionStatus.NotSatisfied || status == ActionStatus.NotLearned)) {
                        index = (CurrentIndex + 1) % ComboActions.Count;
                    } else if (cremain > Configuration.GlobalCoolingDown.TotalSeconds) {
                        index = (CurrentIndex + 1) % ComboActions.Count;
                    }
                    break;
                case ComboType.StrictBlocked:
                    index = CurrentIndex;
                    if (!Actions.Equals(ComboActions[CurrentIndex].ID, actionID))
                        break;
                    if ((status == ActionStatus.Ready || status == ActionStatus.NotLearned)) {
                        index = (CurrentIndex + 1) % ComboActions.Count;
                    }
                    break;
                case ComboType.Linear:
                    if ((status == ActionStatus.Ready || status == ActionStatus.NotSatisfied || status == ActionStatus.NotLearned)) {
                        index = (index + 1) % ComboActions.Count;
                    } else if (Type == ComboType.Linear && cremain > Configuration.GlobalCoolingDown.TotalSeconds) {
                        index = (index + 1) % ComboActions.Count;
                    }
                    break;
                case ComboType.LinearBlocked:
                    if ((status == ActionStatus.Ready || status == ActionStatus.NotLearned)) {
                        index = (index + 1) % ComboActions.Count;
                    }
                    break;
                case ComboType.Ochain:
                    // usually by GamepadActionManager.UpdateFramework
                    // or actions in other group
                    if (originalIndex == -1) {
                        switch (caction.Type)
                        {
                            // 跳NotSatisfied需要确保action已经执行
                            case ComboActionType.SingleSkipable:
                            case ComboActionType.MultiSkipable:
                                if (cstatus == ActionStatus.NotSatisfied && caction.Executed || cstatus == ActionStatus.Pending && cremain > Configuration.GlobalCoolingDown.TotalSeconds) {
                                    index += 1; caction.Restore();
                                }
                                break;
                            // 全跳
                            case ComboActionType.Skipable:
                                if (cstatus == ActionStatus.NotSatisfied || cstatus == ActionStatus.Pending && cremain > Configuration.GlobalCoolingDown.TotalSeconds) {
                                    index += 1;
                                }
                                break;
                        }

                        animationDelay = 0;
                    } else {
                        switch (caction.Type)
                        {
                            case ComboActionType.Skipable:
                            case ComboActionType.Single:
                            case ComboActionType.SingleSkipable:
                            case ComboActionType.Multi:
                            case ComboActionType.MultiSkipable:
                                if (cstatus == ActionStatus.Pending && cremain > Configuration.GlobalCoolingDown.TotalSeconds) {
                                    index += 1; animationDelay = 0;
                                } else if (cstatus == ActionStatus.Ready) {
                                    if (succeed) {
                                        caction.Count += 1; caction.Executed = true;
                                    }
                                    if (caction.Count >= caction.MaximumCount) {
                                        index += 1; caction.Restore();
                                    } else if (caction.Count >= caction.MinimumCount) {
                                        var naction = ComboActions[(index+1)%ComboActions.Count];
                                        var nadjust = Actions.AdjustedActionID(naction.ID);
                                        var nstatus = Actions.ActionStatus(nadjust);
                                        var nrgroup = Actions.RecastGroup(nadjust);
                                        var nremain = Actions.RecastTimeRemain(nadjust);
                                        if (nstatus == ActionStatus.Ready || nrgroup == crgroup || nstatus == ActionStatus.Pending && nremain <= Configuration.GlobalCoolingDown.TotalSeconds) {
                                            index += 1; caction.Restore();
                                        }
                                    }
                                } else if (cstatus == ActionStatus.NotSatisfied) {
                                    if (caction.Type == ComboActionType.Single || caction.Type == ComboActionType.Multi) {
                                        if (caction.Executed) {
                                            index += 1; caction.Restore(); animationDelay = 0;
                                        } else {
                                            caction.Executed = true;    // <---- 防止卡住. 点两次
                                        }
                                    } else if (caction.Type == ComboActionType.SingleSkipable || caction.Type == ComboActionType.MultiSkipable || caction.Type == ComboActionType.Skipable) {
                                        index += 1; caction.Restore(); animationDelay = 0;  // 点一次
                                    }
                                }
                                break;
                            case ComboActionType.Blocking:  // Pending ?
                                if (cstatus == ActionStatus.Ready) {
                                    if (succeed) index += 1;
                                } else {
                                    caction.Count += 1;
                                    if (caction.Count >= 5) {   // <--- 点五次
                                        index += 1; caction.Restore();
                                    }
                                }
                                break;
                        }

                        PluginLog.Debug($"[ComboStateUpdate] Group: {GroupID}, CurrentIndex: {CurrentIndex}, origIndex: {originalIndex}, index: {index}, action: {Actions[caction.ID]} {caction.Executed}, status: {cstatus}, type: {caction.Type} , crgroup: {crgroup}, remain: {cremain}");
                    }
                    break;
                case ComboType.Async:
                    // TODO
                    break;
                default:
                    break;
            }

            // 同一个Action可能会以不跳的Status被多次执行
            // *a1 a2 a3
            // a1! && Ready -> CurrentIndex++
            // wait 20ms
            // a! && Pending -> ??? 应该忽略这个
            if (originalIndex != -1 && originalIndex != index) {
                var me = Plugin.Player;
                if (me is not null && me.IsCasting && animationDelay > 0) {
                    animationDelay = Math.Max((int)((me.TotalCastTime - me.CurrentCastTime) * 1000) + 100, animationDelay);
                }

                if (animationDelay > 0)
                    PluginLog.Debug($"animationDelay: {animationDelay}");

                // 反正能力技之间的插入也有时间间隔, 不如等一等, 放动画
                await Task.Delay(animationDelay);

                CurrentIndex = index % ComboActions.Count;

                // CurrentIndex更新之后不代表就立刻转移到了下一个技能, 到GetIcon更新技能图标还有一段时间.
                // 继续sleep, 到图标更新完成.
                await Task.Delay(100);

                // if (cstatus == ActionStatus.Ready)
                this.LastTime = DateTime.Now;
            }

            this.actionLockHighPriority.Release();
            this.actionLock.Release();

            return true;
        }
    }

    public class ComboManager
    {
        public Dictionary<uint, ComboGroup> ComboGroups = new Dictionary<uint, ComboGroup>();
        public Actions Actions = new Actions();

        public ComboManager(List<(uint, List<ComboAction>, string)> actions)
        {
            foreach (var (groupID, comboActions, comboType) in actions) {
                // var groupID = i.Key;
                // var comboActions = i.Value;
                var combo = new ComboGroup(groupID, comboActions, comboType);
                ComboGroups.Add(groupID, combo);
            }
        }

        public void StateReset(uint groupID = 0)
        {
            if (groupID == 0) {
                foreach (var i in ComboGroups) {
                    i.Value.StateReset();
                }
            } else if (ComboGroups.ContainsKey(groupID)) {
                ComboGroups[groupID].StateReset();
            }
        }

        public async Task<bool> StateUpdate(uint actionID, ActionStatus status = ActionStatus.Ready, bool succeed = true, DateTime timestamp = default(DateTime))
        {
            return (await Task.WhenAll(ComboGroups.Select(i => i.Value.StateUpdate(actionID, status, succeed, timestamp)).ToArray())).Any();
        }

        public uint Current(uint groupID, uint lastComboAction = 0, float comboTimer = 0f) => ComboGroups.ContainsKey(groupID) ? ComboGroups[groupID].Current(lastComboAction, comboTimer) : groupID;
        public bool Contains(uint actionID) => ComboGroups.Any(x => x.Value.Contains(actionID));
        public bool ContainsGroup(uint actionID) => ComboGroups.ContainsKey(actionID);
    }
}