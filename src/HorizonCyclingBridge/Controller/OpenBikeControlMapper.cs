using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HorizonCyclingBridge.Controller
{
    /// <summary>
    /// OpenBikeControl コントローラー操作から生成される仮想ゲームパッド操作
    /// </summary>
    public class ControllerAction
    {
        /// <summary>
        /// 割り当て先 ("Button1"〜"Button32", "Brake", "DifficultyUp" 等)
        /// </summary>
        public string Target { get; set; } = string.Empty;

        /// <summary>
        /// 押下 (true) / 解放 (false)
        /// </summary>
        public bool Pressed { get; set; }

        /// <summary>
        /// ブレーキ強度 (0.0 から 1.0)。ブレーキ以外は押下中 1.0
        /// </summary>
        public float Value { get; set; }
    }

    /// <summary>
    /// OpenBikeControl ボタン状態メッセージ (0x01) を解析し、割り当て済みの操作へ変換する純粋ロジック
    /// 1つの物理ボタンが複数のIDを同一メッセージで送る場合があるため、同一メッセージ内で新たに押されたIDを1回の押下として扱い、
    /// 優先順位 (シフト > ブレーキ > 難易度 > ナビゲーション > 決定) で1つの操作だけを実行する
    /// </summary>
    public class OpenBikeControlMapper
    {
        public const byte MSG_TYPE_BUTTON_STATE = 0x01;
        public const byte BUTTON_SHIFT_UP = 0x01;
        public const byte BUTTON_SHIFT_DOWN = 0x02;
        public const byte BUTTON_SELECT = 0x14;
        public const byte BUTTON_BRAKE = 0x1A;

        // ギア直接指定・ステアリング角度は押下/解放ではなく連続値のため押下判定から除外
        private static readonly HashSet<byte> AnalogOnlyIds = new HashSet<byte> { 0x03, 0x04, 0x05, 0x1B };

        public const string TARGET_NONE = "None";
        public const string TARGET_BRAKE = "Brake";

        /// <summary>
        /// config.json で使用するボタン名と OpenBikeControl ボタンIDの対応表
        /// </summary>
        public static readonly IReadOnlyDictionary<string, byte> ButtonIds = new Dictionary<string, byte>
        {
            ["ShiftUp"] = 0x01,
            ["ShiftDown"] = 0x02,
            ["Up"] = 0x10,
            ["Down"] = 0x11,
            ["Left"] = 0x12,
            ["Right"] = 0x13,
            ["Select"] = 0x14,
            ["Back"] = 0x15,
            ["Menu"] = 0x16,
            ["Home"] = 0x17,
            ["Brake"] = 0x1A,
            ["IncreaseDifficulty"] = 0x30,
            ["DecreaseDifficulty"] = 0x31,
            ["SkipInterval"] = 0x32,
            ["Pause"] = 0x33,
            ["Resume"] = 0x34,
            ["PreviousInterval"] = 0x36
        };

        /// <summary>
        /// 既定のボタン割り当て (ButtonN は vJoy ボタン番号。x360ce で Xbox ボタンに割り当てる)
        /// Button1=A, Button2=B, Button3=X, Button4=Y, Button5=Start, Button6=Back(View), Button7=LB, Button8=RB を想定
        /// </summary>
        public static Dictionary<string, string> CreateDefaultMap()
        {
            return new Dictionary<string, string>
            {
                ["ShiftUp"] = "Button2",            // B: シフトアップ (Forza 既定)
                ["ShiftDown"] = "Button3",          // X: シフトダウン (Forza 既定)
                ["Brake"] = TARGET_BRAKE,           // LT: ブレーキ軸
                ["IncreaseDifficulty"] = "DifficultyUp",
                ["DecreaseDifficulty"] = "DifficultyDown",
                ["SkipInterval"] = "WorkoutSkip",
                ["PreviousInterval"] = "WorkoutPrevious",
                ["Pause"] = "WorkoutPause",
                ["Resume"] = "WorkoutResume",
                ["Left"] = "Button7",               // LB: メニューのタブ切替
                ["Right"] = "Button8",              // RB: メニューのタブ切替
                ["Select"] = "Button1",             // A: 決定
                ["Back"] = "Button2",               // B: 戻る
                ["Menu"] = "Button5",               // Start: ポーズメニュー
                ["Home"] = "Button6"                // Back(View): マップ
            };
        }

        private class ButtonPress
        {
            public byte WinnerId;
            public string Target = string.Empty;
            public HashSet<byte> HeldIds = new HashSet<byte>();
        }

        private readonly Dictionary<byte, string> _targets = new Dictionary<byte, string>();
        private readonly Dictionary<byte, byte> _states = new Dictionary<byte, byte>();
        private readonly List<ButtonPress> _activePresses = new List<ButtonPress>();

        public OpenBikeControlMapper(IDictionary<string, string>? buttonMap = null)
        {
            var map = buttonMap ?? CreateDefaultMap();
            foreach (var kv in map)
            {
                if (ButtonIds.TryGetValue(kv.Key, out byte id) && !string.IsNullOrWhiteSpace(kv.Value) && !string.Equals(kv.Value, TARGET_NONE, StringComparison.OrdinalIgnoreCase))
                {
                    _targets[id] = kv.Value.Trim();
                }
            }
        }

        /// <summary>
        /// 割り当て済みのボタンID一覧 (App Information メッセージ送信用)
        /// </summary>
        public IReadOnlyList<byte> SupportedButtonIds => _targets.Keys.OrderBy(id => id).ToList();

        /// <summary>
        /// 優先順位 (小さいほど優先): シフト > ブレーキ > 難易度・トレーニング操作 > ナビゲーション > 決定
        /// </summary>
        public static int GetPriority(byte buttonId)
        {
            if (buttonId == BUTTON_SHIFT_UP || buttonId == BUTTON_SHIFT_DOWN) return 0;
            if (buttonId == BUTTON_BRAKE) return 1;
            if (buttonId >= 0x30 && buttonId <= 0x3F) return 2;
            if (buttonId == BUTTON_SELECT) return 4;
            if (buttonId >= 0x10 && buttonId <= 0x1F) return 3;
            return 5;
        }

        /// <summary>
        /// ブレーキのアナログ値を 0.0〜1.0 に変換 (0x01 = 100%, 0x02〜0xC9 = 値-1 %, 100%超はクランプ)
        /// </summary>
        public static float ParseBrakeStrength(byte state)
        {
            if (state == 0x00) return 0.0f;
            if (state == 0x01) return 1.0f;
            return Math.Min(1.0f, (state - 1) / 100.0f);
        }

        /// <summary>
        /// ボタン状態メッセージを解析し、発生した押下・解放操作を返す
        /// </summary>
        public List<ControllerAction> ProcessMessage(byte[] data)
        {
            var actions = new List<ControllerAction>();
            if (data == null || data.Length < 3 || data[0] != MSG_TYPE_BUTTON_STATE) return actions;

            var newlyPressed = new List<byte>();
            for (int i = 1; i + 1 < data.Length; i += 2)
            {
                byte id = data[i];
                byte state = data[i + 1];
                if (AnalogOnlyIds.Contains(id)) continue;

                bool wasPressed = _states.TryGetValue(id, out byte prevState) && prevState != 0x00;
                bool isPressed = state != 0x00;
                _states[id] = state;

                if (isPressed && !wasPressed)
                {
                    newlyPressed.Add(id);
                }
                else if (!isPressed && wasPressed)
                {
                    HandleRelease(id, actions);
                }
                else if (isPressed && prevState != state)
                {
                    // 押下中のアナログ値変化 (ブレーキ強度の更新)
                    var press = _activePresses.FirstOrDefault(p => p.WinnerId == id);
                    if (press != null && press.Target == TARGET_BRAKE)
                    {
                        actions.Add(new ControllerAction { Target = press.Target, Pressed = true, Value = GetValue(id, state) });
                    }
                }
            }

            if (newlyPressed.Count > 0)
            {
                var mapped = newlyPressed.Where(id => _targets.ContainsKey(id)).ToList();
                if (mapped.Count > 0)
                {
                    // 同一メッセージで押されたIDのうち、最も優先度の高いIDの操作のみ実行 (同順位はメッセージ順)
                    byte winner = mapped.OrderBy(GetPriority).First();
                    var press = new ButtonPress
                    {
                        WinnerId = winner,
                        Target = _targets[winner],
                        HeldIds = new HashSet<byte>(newlyPressed)
                    };
                    _activePresses.Add(press);
                    actions.Add(new ControllerAction { Target = press.Target, Pressed = true, Value = GetValue(winner, _states[winner]) });
                }
            }

            return actions;
        }

        /// <summary>
        /// 切断時などに押下中の操作をすべて解放
        /// </summary>
        public List<ControllerAction> Reset()
        {
            var actions = _activePresses.Select(p => new ControllerAction { Target = p.Target, Pressed = false, Value = 0.0f }).ToList();
            _activePresses.Clear();
            _states.Clear();
            return actions;
        }

        /// <summary>
        /// App Information メッセージ (0x04) を生成
        /// [0x04] [Version=0x01] [App_ID_Length] [App_ID...] [App_Version_Length] [App_Version...] [Button_Count] [Button_IDs...]
        /// </summary>
        public static byte[] BuildAppInfo(string appId, string appVersion, IReadOnlyList<byte> buttonIds)
        {
            byte[] idBytes = Encoding.UTF8.GetBytes(appId);
            byte[] versionBytes = Encoding.UTF8.GetBytes(appVersion);
            if (idBytes.Length > 32) idBytes = idBytes.Take(32).ToArray();
            if (versionBytes.Length > 32) versionBytes = versionBytes.Take(32).ToArray();

            var data = new List<byte> { 0x04, 0x01, (byte)idBytes.Length };
            data.AddRange(idBytes);
            data.Add((byte)versionBytes.Length);
            data.AddRange(versionBytes);
            data.Add((byte)buttonIds.Count);
            data.AddRange(buttonIds);
            return data.ToArray();
        }

        private void HandleRelease(byte id, List<ControllerAction> actions)
        {
            foreach (var press in _activePresses.ToList())
            {
                if (!press.HeldIds.Remove(id)) continue;

                // 1回の押下に含まれるIDがすべて離されたら操作を解放
                if (press.HeldIds.Count == 0)
                {
                    _activePresses.Remove(press);
                    actions.Add(new ControllerAction { Target = press.Target, Pressed = false, Value = 0.0f });
                }
            }
        }

        private float GetValue(byte id, byte state)
        {
            return id == BUTTON_BRAKE ? ParseBrakeStrength(state) : 1.0f;
        }
    }
}
