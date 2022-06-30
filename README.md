Using `y/b/a/x` and digipad `up/right/down/left` buttons to directly select party member no.1-8.

❗: Should use the default ClassJob sort order in game settings.

# Usage
**Step 1**: Press a button to trigger an monitored action, like `均衡诊断`, not execute it.

**Step 2**: Press a button to select a party member and execute that action to the target immediately.

❗: Those button states are only captured **when CrossHotBar is activated (press LT/RT)**

```
/gi → Open setting panel.
/gi list → List actions and corresponding selection order.
/gi add <action> [<selectOrder>] → Add specific <action> in monitor.
/gi remove <action> → Remove specific monitored <action>.

<action>        Action name (in string).
<selectOrder>   The order for party member selection (only accepted Digipad and y/b/a/x buttons).
   Xbox |   PS
    y   |   △   |   n:North
    b   |   ○   |   e:East
    a   |   x   |   s:South
    x   |   □   |   w:West
```

# Config
```jsonc
{
  "debug": false,
  "actionsInMonitor": [
    "均衡诊断",
    "白牛清汁",
    "灵橡清汁",
    "输血"
  ],

  // Should restore to the default ClassJob sort order in game settings first in order to get the right mapping.
  // y → left <=> party member no.1 → no.8
  "selectOrder": "y b a x right down up left",

  // thmr: Tank Healer MeleeDPS RangedDPS
  // RangedDPS are in [PhysicalRangedDPS, MagicalRangedDPS] order by default in game settings.
  // Could use other class sort strategy like "htmr", "mrth", "tmrh", etc.
  "partyMemeberSortOrder": "thmr",

  // Key: Action / ActionID
  // Val: SelectOrder string, will use `selectOrder` value in config if val is "" or "default".
  "rules": {
    // Use a different select order for specific action.
    "吉星相位": "y a x b up down left right",

    // Could add other actions which this plugin are not built-in.
    // ActionID: 16556 → 天星交错
    "16556": "default",
  }
}
```

# Pre-included Actions
```csharp
{
    {"诊断", 24284},
    // 均衡诊断是24291, 但UseAction的参数ActionID却使用的是24284
    {"均衡诊断", 24284},
    {"白牛清汁", 24303},
    {"灵橡清汁", 24296},
    {"混合", 24317},
    {"输血", 24305},

    {"神祝祷", 7432},
    {"神名", 3570},
    {"水流幕", 25861},
    {"安慰之心", 16531},

    {"先天禀赋", 3614},
    {"出卡", 17055},
    {"吉星相位", 3595},
    {"星位合图", 8918},
    {"出王冠卡", 25869},
    {"天星交错", 16556},
    {"擢升", 25873}

    {"鼓舞激励之策", 185},
    {"生命活性法", 189},
    {"深谋远虑之策", 7434},
    {"以太契约", 7423},
    {"生命回生法", 25867}
};
```