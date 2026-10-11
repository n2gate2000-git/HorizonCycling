/**
 * HorizonCycling Studio - Multi-language (i18n) Engine
 * Supported languages: Japanese ('ja'), English ('en')
 */

(function () {
  'use strict';

  const translations = {
    ja: {
      app: {
        title: "HorizonCycling Studio",
        tagline: "Forza Horizon 6 Virtual Cycling"
      },
      header: {
        modeSimulation: "SIMULATION (リアル実走)",
        modeArcade: "ARCADE (パワー直結)",
        overlayBtn: "🖥️ 最前面HUD",
        overlayBtnTitle: "Forza Horizonの最前面にZwift風HUDを表示/非表示 (Ctrl+Shift+O)",
        saveBtn: "💾 保存",
        saveBtnTitle: "走行を終了してGPX/FITログを保存",
        settingsBtnTitle: "設定",
        langBtnTitle: "表示言語を切り替え (Switch Language)"
      },
      nav: {
        map: "🗺 リアルタイムマップ追跡",
        profile: "📈 走行プロファイル",
        workouts: "🏋️ ワークアウト",
        history: "📜 走行履歴",
        logs: "📋 ログ・セッション詳細"
      },
      metrics: {
        power: "POWER",
        cadence: "CADENCE",
        speed: "SPEED",
        actualSpeed: "実車速",
        targetSpeed: "目標速度 (PID)",
        grade: "GRADE",
        elevation: "ELEVATION",
        heartRate: "HEART RATE",
        calories: "CALORIES",
        distance: "DISTANCE",
        time: "TIME",
        throttle: "THROTTLE",
        brake: "BRAKE",
        sentGrade: "送信: {grade}%",
        gain: "獲得: +{gain} m",
        notConnected: "未接続",
        connected: "接続中",
        disconnected: "切断",
        z1Recovery: "Z1 回復 (<60%)",
        z2Aerobic: "Z2 有酸素 (60-69%)",
        z3Tempo: "Z3 テンポ (70-79%)",
        z4Threshold: "Z4 閾値 (80-89%)",
        z5Anaerobic: "Z5 無酸素 (90%+)"
      },
      map: {
        followCar: "📍 自車追従: {state}",
        pins: "📌 スポット: {state}",
        calibrate: "🎯 位置合わせ",
        resetView: "🔍 全体表示",
        clearTrack: "🧹 軌跡クリア",
        calibrateBanner: "🎯 地図上の「現在走行している場所（道路やスポット）」をクリックしてください",
        cancel: "✕ 取消"
      },
      profile: {
        currentElev: "現在標高",
        speedTarget: "速度 / 目標",
        power: "パワー",
        heartRate: "心拍数",
        elevationGain: "獲得標高",
        totalDistance: "総走行距離",
        calories: "消費カロリー",
        recentBtn: "🔍 直近",
        recentBtnTitle: "直近プロファイルを表示",
        recentDistanceTitle: "直近表示の距離を選択 (100m〜1000m)",
        allBtn: "🗺 全走行距離",
        allBtnTitle: "スタートからの全走行プロファイルを表示"
      },
      workout: {
        searchPlaceholder: "🔍 ワークアウト検索...",
        rampTestBtn: "⚡ ランプテスト(FTP計測)",
        rampTestBtnTitle: "FTPを自動計測するランプテストを開始",
        importBtn: "📂 取込",
        importBtnTitle: "ワークアウトファイルを取り込む",
        folderBtn: "📁 フォルダ",
        folderBtnTitle: "ワークアウト保存フォルダを開く",
        refreshBtnTitle: "ライブラリ更新",
        catAll: "すべて",
        catRecovery: "回復 (Recovery)",
        catEndurance: "有酸素 (Endurance)",
        catTempo: "テンポ (Tempo)",
        catThreshold: "FTP・閾値 (Threshold)",
        catVo2max: "VO2Max",
        catAnaerobic: "無酸素 (Anaerobic)",
        catRamp: "ランプテスト",
        selectPromptTitle: "ワークアウトを選択してください",
        selectPromptDesc: "左のリストから挑戦したいワークアウトを選ぶと、ここに詳細と負荷グラフが表示されます。",
        totalTime: "総時間",
        estTss: "予測 TSS",
        intervals: "ブロック数",
        author: "作成者",
        startBtn: "▶ ワークアウト開始",
        pauseBtn: "⏸ 一時停止",
        resumeBtn: "▶ 再開",
        stopBtn: "⏹ ワークアウト終了",
        prevStepBtnTitle: "前のブロックへ (⏮)",
        skipStepBtnTitle: "次のブロックへ (⏭)",
        warmup: "ウォームアップ",
        interval: "インターバル",
        recovery: "回復",
        cooldown: "クールダウン",
        rampStep: "ランプステップ",
        activeWorkout: "アクティブ・ワークアウト",
        remaining: "残り時間",
        targetWatts: "目標パワー",
        targetCadence: "目標ケイデンス",
        currentBlock: "現在のブロック",
        nextBlock: "次のブロック",
        blockProgress: "ブロック進捗",
        completedBannerTitle: "🎉 ワークアウト完了！",
        completedBannerDesc: "素晴らしいペダリングでした！本日のトレーニング目標をすべて達成しました。"
      },
      history: {
        totalRides: "総ライド数",
        totalDistance: "総走行距離",
        totalTime: "総走行時間",
        totalElevationGain: "総獲得標高",
        avgPower: "平均パワー",
        searchPlaceholder: "🔍 走行履歴を検索...",
        sidebarTitle: "📜 走行ログ一覧",
        refreshBtn: "🔄 更新",
        refreshBtnTitle: "履歴を再読込",
        openFolderBtn: "📂 フォルダ",
        openFolderBtnTitle: "ログ保存フォルダを開く",
        loading: "履歴を読み込み中...",
        emptyPrompt: "左側のリストから走行記録を選択してください",
        openGpxBtn: "📤 GPXを開く",
        deleteBtn: "🗑 削除",
        statTime: "⏱ 走行時間",
        statDistance: "📏 走行距離",
        statElevationGain: "⛰ 獲得標高",
        statAvgPower: "⚡ 平均パワー",
        statHeartRate: "❤️ 心拍数",
        statCalories: "🔥 消費エネルギー",
        hrZoneSectionTitle: "❤️ 心拍ゾーン分布 (HR Zone Distribution)",
        mapSectionTitle: "🗺 走行ルート軌跡 (Forza Horizon 6 マップ)",
        followPointBtn: "📍 自動追従: {state}",
        fitRouteBtn: "🔍 全体表示",
        fitRouteBtnTitle: "ルート全体を表示領域にフィット",
        chartSectionTitle: "📈 走行プロファイル推移 (高度 / 速度 / 目標 / パワー)",
        filterAll: "すべて",
        filterFreeride: "フリーライド",
        filterWorkout: "ワークアウト",
        filterRamp: "ランプテスト",
        colDate: "日時",
        colType: "種別",
        colTitle: "タイトル",
        colDist: "距離",
        colDuration: "時間",
        colAvgPower: "平均パワー",
        colMaxPower: "最高パワー",
        colAvgSpeed: "平均速度",
        colGain: "獲得標高",
        colActions: "操作",
        exportGpx: "GPX",
        exportFit: "FIT",
        openFolder: "フォルダ",
        openFolderTitle: "ログ保存フォルダを開く",
        deleteBtnTitle: "走行ログを削除",
        deleteConfirm: "この走行ログを削除しますか？\n（ファイルも削除されます）",
        emptyText: "保存された走行ログはありません",
        modalTitle: "アクティビティ詳細",
        modalClose: "閉じる",
        chartElevation: "標高 (m)",
        chartSpeed: "速度 (km/h)",
        chartTargetSpeed: "目標速度 (km/h)",
        chartPower: "パワー (W)",
        chartHeartRate: "心拍数 (bpm)",
        chartAxisDist: "走行距離 (km)",
        chartTooltipDist: "距離: {val}",
        chartTooltipEle: " 標高: {val} m",
        chartTooltipSpd: " 速度: {val} km/h",
        chartTooltipTgt: " 目標速度: {val} km/h",
        chartTooltipPwr: " パワー: {val} W",
        chartTooltipHr: " 心拍数: {val} bpm",
        hrSummary: "計測: {dur} | 平均: {avg} bpm | 最大: {max} bpm"
      },
      logs: {
        sessionCardTitle: "現在のセッション情報",
        clearBtn: "🧹 ログ消去",
        status: "セッション状態",
        elapsed: "経過時間",
        distance: "総走行距離",
        avgSpeed: "平均速度",
        maxSpeed: "最高速度",
        avgPower: "平均パワー",
        maxPower: "最高パワー",
        avgCadence: "平均ケイデンス",
        avgHr: "平均心拍数",
        maxHr: "最高心拍数",
        packets: "UDP受信パケット数"
      },
      settings: {
        modalTitle: "⚙ システム & センサー設定",
        sectionLanguage: "🌐 言語 / Language",
        languageLabel: "表示言語 (Display Language)",
        sectionTrainer: "🚴 スマートローラー (負荷制御 / トレーナー)",
        scanTrainerBtn: "🔍 デバイスを検索",
        currentConfigured: "現在設定中:",
        none: "None",
        scanningTrainer: "周囲のBluetoothスマートローラーを検索中 (約7秒)...",
        emptyTrainer: "「デバイスを検索」ボタンを押して、スマートローラーをスキャンしてください。",
        connectTrainerBtn: "選択したデバイスに接続",
        sectionCadence: "⚡ ケイデンス / パワーメーター (Magene PES-P505 等)",
        scanCadenceBtn: "🔍 センサーを検索",
        disconnectBtn: "切断",
        scanningCadence: "周囲のケイデンス/パワーメーターを検索中 (約7秒)...",
        emptyCadence: "「センサーを検索」ボタンを押して、Magene PES-P505等をスキャンしてください。",
        connectCadenceBtn: "選択したセンサーに接続",
        cadenceWakeHint: "💡 クランクを数回転回してパワーメーターを起動(スリープ解除)してから接続してください",
        preferPowerMeterLabel: "パワー値もこのパワーメーターを優先",
        preferPowerMeterTitle: "パワー値もスマートトレーナーではなくパワーメーターの正確な数値を優先して使用します",
        sectionHr: "❤️ Bluetooth 心拍計 (Heart Rate Monitor)",
        scanHrBtn: "🔍 心拍計を検索",
        scanningHr: "周囲のBluetooth心拍計を検索中 (約7秒)...",
        emptyHr: "「心拍計を検索」ボタンを押してスキャンしてください。",
        connectHrBtn: "選択した心拍計に接続",
        sectionCycling: "⚙ サイクリング設定",
        trainerDifficulty: "トレーナー難易度 / 負荷再現割合 (0〜100%)",
        diffPresetStandard: "50% (標準)",
        diffPresetRealistic: "100% (実勾配)",
        riderFtp: "ライダー FTP (W)",
        riderWeight: "ライダー体重 (kg)",
        riderMaxHr: "ライダー最大心拍数 (bpm)",
        riderRestingHr: "ライダー安静時心拍数 (bpm)",
        recentDistance: "プロファイル直近表示距離",
        pedalBrake: "ペダル自動ブレーキ (脚を止めたら制動)",
        freerideOverlay: "通常走行時も最前面HUDオーバーレイを表示",
        sectionSound: "🔊 ワークアウト音響・通知設定",
        soundEnabled: "効果音を有効化",
        soundVolume: "効果音音量 (0〜100%)",
        transitionSound: "セグメント切り替え音 (Work/Rest)",
        countdownSound: "3秒前カウントダウン予告音 (3, 2, 1)",
        ttsEnabled: "音声ガイダンス(TTS)によるテキスト読み上げ",
        testWorkBtn: "🔊 Work開始音",
        testRestBtn: "🔊 Rest回復音",
        testCountdownBtn: "🔊 カウントダウン",
        testTtsBtn: "🗣️ 音声テスト",
        testTtsText: "これは音声ガイダンスのテストです。目標パワーを維持してください。",
        sectionMap: "🗺️ マップ座標・縮尺設定",
        mapScale: "地図縮尺 (スケール: 0.150〜0.400 px/m, 0.001刻み)",
        scalePresetOptimal: "0.252 (実寸最適)",
        scalePresetStandard: "0.263 (標準)",
        invertZ: "Z軸反転 (上下の進行方向を反転)",
        resetOriginBtn: "原点を初期位置 (4096, 4096) にリセット",
        saveBtn: "設定を保存して閉じる"
      },
      rampModal: {
        title: "🎉 FTPランプテスト完了！",
        newFtp: "新測定FTP",
        prevFtp: "前回のFTP",
        best1Min: "最高1分間パワー",
        duration: "総テスト時間",
        desc: "お疲れ様でした！新しく測定されたFTPをシステム設定に自動反映しました。今後のワークアウトはこのFTPを基準に負荷が自動調整されます。",
        closeBtn: "閉じる"
      },
      toast: {
        sessionSaved: "走行セッションを保存しました: {file}",
        saveFailed: "保存に失敗しました: {error}",
        settingsSaved: "設定を保存しました",
        connecting: "接続中: {name}...",
        connected: "{name} に接続しました",
        disconnected: "切断しました",
        failedToConnect: "接続に失敗しました: {error}",
        originReset: "原点を (4096, 4096) にリセットしました",
        calibrated: "自車位置をキャリブレーションしました"
      }
    },
    en: {
      app: {
        title: "HorizonCycling Studio",
        tagline: "Forza Horizon 6 Virtual Cycling"
      },
      header: {
        modeSimulation: "SIMULATION (Realistic)",
        modeArcade: "ARCADE (Direct Power)",
        overlayBtn: "🖥️ Topmost HUD",
        overlayBtnTitle: "Show/hide Zwift-style topmost HUD in Forza Horizon (Ctrl+Shift+O)",
        saveBtn: "💾 Save",
        saveBtnTitle: "End ride and save GPX/FIT activity logs",
        settingsBtnTitle: "Settings",
        langBtnTitle: "Switch Display Language (表示言語を切替)"
      },
      nav: {
        map: "🗺 Realtime Map",
        profile: "📈 Elevation Profile",
        workouts: "🏋️ Workouts",
        history: "📜 Activity History",
        logs: "📋 Logs & Details"
      },
      metrics: {
        power: "POWER",
        cadence: "CADENCE",
        speed: "SPEED",
        actualSpeed: "Actual Speed",
        targetSpeed: "Target Speed (PID)",
        grade: "GRADE",
        elevation: "ELEVATION",
        heartRate: "HEART RATE",
        calories: "CALORIES",
        distance: "DISTANCE",
        time: "TIME",
        throttle: "THROTTLE",
        brake: "BRAKE",
        sentGrade: "Sent: {grade}%",
        gain: "Gain: +{gain} m",
        notConnected: "Disconnected",
        connected: "Connected",
        disconnected: "Disconnected",
        z1Recovery: "Z1 Active Recovery (<60%)",
        z2Aerobic: "Z2 Endurance (60-69%)",
        z3Tempo: "Z3 Tempo (70-79%)",
        z4Threshold: "Z4 Threshold (80-89%)",
        z5Anaerobic: "Z5 Anaerobic (90%+)"
      },
      map: {
        followCar: "📍 Follow Vehicle: {state}",
        pins: "📌 POI Spots: {state}",
        calibrate: "🎯 Calibrate Pos",
        resetView: "🔍 Fit All",
        clearTrack: "🧹 Clear Track",
        calibrateBanner: "🎯 Click the road or spot on the map where you are currently riding",
        cancel: "✕ Cancel"
      },
      profile: {
        currentElev: "Current Elev",
        speedTarget: "Speed / Target",
        power: "Power",
        heartRate: "Heart Rate",
        elevationGain: "Elev Gain",
        totalDistance: "Total Distance",
        calories: "Calories",
        recentBtn: "🔍 Recent",
        recentBtnTitle: "Show recent profile window",
        recentDistanceTitle: "Select recent distance window (100m - 1000m)",
        allBtn: "🗺 Full Route",
        allBtnTitle: "Show full elevation profile from start"
      },
      workout: {
        searchPlaceholder: "🔍 Search workouts...",
        rampTestBtn: "⚡ Ramp Test (FTP)",
        rampTestBtnTitle: "Start automated FTP ramp test",
        importBtn: "📂 Import",
        importBtnTitle: "Import workout files (.zwo, .mrc, .erg)",
        folderBtn: "📁 Folder",
        folderBtnTitle: "Open workouts storage folder",
        refreshBtnTitle: "Refresh workout library",
        catAll: "All",
        catRecovery: "Recovery",
        catEndurance: "Endurance",
        catTempo: "Tempo",
        catThreshold: "Threshold",
        catVo2max: "VO2Max",
        catAnaerobic: "Anaerobic",
        catRamp: "Ramp Test",
        selectPromptTitle: "Select a Workout",
        selectPromptDesc: "Choose a workout from the left library to preview its intervals and power profile.",
        totalTime: "Total Time",
        estTss: "Est. TSS",
        intervals: "Intervals",
        author: "Author",
        startBtn: "▶ Start Workout",
        pauseBtn: "⏸ Pause",
        resumeBtn: "▶ Resume",
        stopBtn: "⏹ Stop Workout",
        prevStepBtnTitle: "Previous Interval (⏮)",
        skipStepBtnTitle: "Next Interval (⏭)",
        warmup: "Warm-up",
        interval: "Interval",
        recovery: "Recovery",
        cooldown: "Cool-down",
        rampStep: "Ramp Step",
        activeWorkout: "Active Workout",
        remaining: "Remaining",
        targetWatts: "Target Watts",
        targetCadence: "Target Cadence",
        currentBlock: "Current Interval",
        nextBlock: "Next Interval",
        blockProgress: "Interval Progress",
        completedBannerTitle: "🎉 Workout Completed!",
        completedBannerDesc: "Outstanding pedaling effort! All training targets have been achieved."
      },
      history: {
        totalRides: "Total Rides",
        totalDistance: "Total Distance",
        totalTime: "Total Time",
        totalElevationGain: "Total Elev Gain",
        avgPower: "Avg Power",
        searchPlaceholder: "🔍 Search history...",
        sidebarTitle: "📜 Activity Logs",
        refreshBtn: "🔄 Refresh",
        refreshBtnTitle: "Reload activity history",
        openFolderBtn: "📂 Folder",
        openFolderBtnTitle: "Open activity storage folder",
        loading: "Loading history...",
        emptyPrompt: "Select an activity log from the list on the left",
        openGpxBtn: "📤 Open GPX",
        deleteBtn: "🗑 Delete",
        statTime: "⏱ Moving Time",
        statDistance: "📏 Distance",
        statElevationGain: "⛰ Elevation Gain",
        statAvgPower: "⚡ Avg Power",
        statHeartRate: "❤️ Heart Rate",
        statCalories: "🔥 Calories",
        hrZoneSectionTitle: "❤️ HR Zone Distribution",
        mapSectionTitle: "🗺 Route Track (Forza Horizon 6 Map)",
        followPointBtn: "📍 Auto-follow: {state}",
        fitRouteBtn: "🔍 Fit Route",
        fitRouteBtnTitle: "Fit entire route into view",
        chartSectionTitle: "📈 Profile Progression (Elev / Speed / Target / Power)",
        filterAll: "All",
        filterFreeride: "Free Ride",
        filterWorkout: "Workout",
        filterRamp: "Ramp Test",
        colDate: "Date & Time",
        colType: "Type",
        colTitle: "Title",
        colDist: "Distance",
        colDuration: "Duration",
        colAvgPower: "Avg Power",
        colMaxPower: "Max Power",
        colAvgSpeed: "Avg Speed",
        colGain: "Gain",
        colActions: "Actions",
        exportGpx: "GPX",
        exportFit: "FIT",
        openFolder: "Folder",
        openFolderTitle: "Open activity storage folder",
        deleteBtnTitle: "Delete activity log",
        deleteConfirm: "Are you sure you want to delete this activity log?\n(Data files will also be removed)",
        emptyText: "No activity logs found",
        modalTitle: "Activity Summary",
        modalClose: "Close",
        chartElevation: "Elevation (m)",
        chartSpeed: "Speed (km/h)",
        chartTargetSpeed: "Target Speed (km/h)",
        chartPower: "Power (W)",
        chartHeartRate: "Heart Rate (bpm)",
        chartAxisDist: "Distance (km)",
        chartTooltipDist: "Distance: {val}",
        chartTooltipEle: " Elev: {val} m",
        chartTooltipSpd: " Speed: {val} km/h",
        chartTooltipTgt: " Target: {val} km/h",
        chartTooltipPwr: " Power: {val} W",
        chartTooltipHr: " HR: {val} bpm",
        hrSummary: "Duration: {dur} | Avg: {avg} bpm | Max: {max} bpm"
      },
      logs: {
        sessionCardTitle: "Current Session Telemetry",
        clearBtn: "🧹 Clear Logs",
        status: "Session Status",
        elapsed: "Elapsed Time",
        distance: "Distance",
        avgSpeed: "Avg Speed",
        maxSpeed: "Max Speed",
        avgPower: "Avg Power",
        maxPower: "Max Power",
        avgCadence: "Avg Cadence",
        avgHr: "Avg HR",
        maxHr: "Max HR",
        packets: "UDP Packets Received"
      },
      settings: {
        modalTitle: "⚙ System & Sensor Settings",
        sectionLanguage: "🌐 Language / 言語",
        languageLabel: "Display Language (表示言語)",
        sectionTrainer: "🚴 Smart Trainer (Resistance Control / FTMS)",
        scanTrainerBtn: "🔍 Scan Devices",
        currentConfigured: "Configured:",
        none: "None",
        scanningTrainer: "Scanning for Bluetooth Smart Trainers (approx. 7s)...",
        emptyTrainer: "Click 'Scan Devices' to discover nearby smart trainers.",
        connectTrainerBtn: "Connect to Selected Device",
        sectionCadence: "⚡ Cadence / Power Meter (Magene etc.)",
        scanCadenceBtn: "🔍 Scan Sensors",
        disconnectBtn: "Disconnect",
        scanningCadence: "Scanning for Cadence / Power Meters (approx. 7s)...",
        emptyCadence: "Click 'Scan Sensors' to discover nearby cadence/power meters.",
        connectCadenceBtn: "Connect to Selected Sensor",
        cadenceWakeHint: "💡 Please spin cranks several times to wake up the power meter before connecting.",
        preferPowerMeterLabel: "Prioritize Power Watts from this Meter",
        preferPowerMeterTitle: "Prioritize power meter watts over smart trainer reading",
        sectionHr: "❤️ Bluetooth Heart Rate Monitor (HRM)",
        scanHrBtn: "🔍 Scan HRM",
        scanningHr: "Scanning for Bluetooth Heart Rate Monitors (approx. 7s)...",
        emptyHr: "Click 'Scan HRM' to discover nearby heart rate monitors.",
        connectHrBtn: "Connect to Selected HRM",
        sectionCycling: "⚙ Cycling Parameters",
        trainerDifficulty: "Trainer Difficulty / Grade Reproduction (0-100%)",
        diffPresetStandard: "50% (Standard)",
        diffPresetRealistic: "100% (Realistic)",
        riderFtp: "Rider FTP (W)",
        riderWeight: "Rider Weight (kg)",
        riderMaxHr: "Rider Max HR (bpm)",
        riderRestingHr: "Rider Resting HR (bpm)",
        recentDistance: "Profile Recent Distance Window",
        pedalBrake: "Auto Pedal Brake (Brake when pedaling stops)",
        freerideOverlay: "Show Topmost HUD Overlay During Free Rides",
        sectionSound: "🔊 Workout Sound & Audio Guidance",
        soundEnabled: "Enable Sound Effects",
        soundVolume: "Sound Volume (0-100%)",
        transitionSound: "Segment Transition Chimes (Work/Rest)",
        countdownSound: "3-Second Countdown Beeps (3, 2, 1)",
        ttsEnabled: "Voice Guidance via Text-to-Speech (TTS)",
        testWorkBtn: "🔊 Test Work Sound",
        testRestBtn: "🔊 Test Rest Sound",
        testCountdownBtn: "🔊 Test Countdown",
        testTtsBtn: "🗣️ Test Voice",
        testTtsText: "This is a voice guidance test. Maintain target power.",
        sectionMap: "🗺️ Map Calibration & Scale",
        mapScale: "Map Scale (0.150 - 0.400 px/m, step 0.001)",
        scalePresetOptimal: "0.252 (Optimal)",
        scalePresetStandard: "0.263 (Standard)",
        invertZ: "Invert Z-Axis (Flip North/South Direction)",
        resetOriginBtn: "Reset Origin to Default (4096, 4096)",
        saveBtn: "Save Settings & Close"
      },
      rampModal: {
        title: "🎉 Ramp Test Complete!",
        newFtp: "New Measured FTP",
        prevFtp: "Previous FTP",
        best1Min: "Best 1-min Power",
        duration: "Test Duration",
        desc: "Great effort! Your newly measured FTP has been automatically applied to system settings. Upcoming workouts will calibrate targets according to this FTP.",
        closeBtn: "Close"
      },
      toast: {
        sessionSaved: "Activity session saved: {file}",
        saveFailed: "Failed to save: {error}",
        settingsSaved: "Settings saved successfully",
        connecting: "Connecting: {name}...",
        connected: "Connected: {name}",
        disconnected: "Disconnected",
        failedToConnect: "Connection failed: {error}",
        originReset: "Map origin reset to (4096, 4096)",
        calibrated: "Vehicle position calibrated"
      }
    }
  };

  class I18nManager {
    constructor() {
      const stored = localStorage.getItem('horizon_language');
      this.currentLang = stored === 'en' ? 'en' : 'ja';
    }

    getLanguage() {
      return this.currentLang;
    }

    setLanguage(lang, notify = true) {
      const normalized = (lang && lang.toLowerCase().startsWith('en')) ? 'en' : 'ja';
      this.currentLang = normalized;
      try {
        localStorage.setItem('horizon_language', normalized);
      } catch (e) {
        console.warn('[i18n] Failed to save to localStorage:', e);
      }

      this.updateDOM();

      if (notify) {
        window.dispatchEvent(new CustomEvent('languageChanged', {
          detail: { language: normalized }
        }));
      }
    }

    t(keyPath, params = {}) {
      const keys = keyPath.split('.');
      let val = translations[this.currentLang];
      for (const k of keys) {
        if (val && typeof val === 'object' && k in val) {
          val = val[k];
        } else {
          // Fallback to Japanese if missing in current
          let fb = translations.ja;
          for (const fbk of keys) {
            if (fb && typeof fb === 'object' && fbk in fb) fb = fb[fbk];
            else { fb = undefined; break; }
          }
          val = fb !== undefined ? fb : keyPath;
          break;
        }
      }

      if (typeof val === 'string') {
        let result = val;
        for (const [pk, pv] of Object.entries(params)) {
          result = result.replace(new RegExp(`\\{${pk}\\}`, 'g'), pv);
        }
        return result;
      }
      return val || keyPath;
    }

    updateDOM(root = document) {
      // 1. Text content
      const elements = root.querySelectorAll('[data-i18n]');
      elements.forEach(el => {
        const key = el.getAttribute('data-i18n');
        if (key) {
          el.textContent = this.t(key);
        }
      });

      // 2. Titles (tooltips)
      const titleEls = root.querySelectorAll('[data-i18n-title]');
      titleEls.forEach(el => {
        const key = el.getAttribute('data-i18n-title');
        if (key) {
          el.setAttribute('title', this.t(key));
        }
      });

      // 3. Placeholders
      const placeholderEls = root.querySelectorAll('[data-i18n-placeholder]');
      placeholderEls.forEach(el => {
        const key = el.getAttribute('data-i18n-placeholder');
        if (key) {
          el.setAttribute('placeholder', this.t(key));
        }
      });

      // 4. Update language toggle button text if present
      const langBtn = document.getElementById('btnLangToggle');
      if (langBtn) {
        langBtn.textContent = this.currentLang === 'en' ? '🌐 English' : '🌐 日本語';
        langBtn.setAttribute('title', this.t('header.langBtnTitle'));
      }

      // 5. Update settings select if present
      const langSelect = document.getElementById('settingLanguage');
      if (langSelect && langSelect.value !== this.currentLang) {
        langSelect.value = this.currentLang;
      }

      // Update HTML lang attribute
      document.documentElement.lang = this.currentLang;
    }

    init() {
      this.updateDOM();
    }
  }

  // Export globally
  window.i18n = new I18nManager();

  document.addEventListener('DOMContentLoaded', () => {
    window.i18n.init();
  });
})();
