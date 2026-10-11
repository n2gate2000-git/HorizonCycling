/**
 * HorizonCycling Studio - Frontend Controller & C# Bridge Interop
 */

document.addEventListener('DOMContentLoaded', () => {
  // マップとチャートの初期化
  const mapTracker = new LiveMapTracker('mapView');
  mapTracker.init();

  const elevationChart = new ElevationProfileChart('elevationChart');
  elevationChart.init();

  const historyController = new SessionHistoryController();
  historyController.init();
  window.historyController = historyController;

  // DOM要素の参照
  const powerVal = document.getElementById('powerVal');
  const wkgVal = document.getElementById('wkgVal');
  const powerZoneIndicator = document.getElementById('powerZoneIndicator');
  const carSpeedVal = document.getElementById('carSpeedVal');
  const targetSpeedVal = document.getElementById('targetSpeedVal');
  const gradeVal = document.getElementById('gradeVal');
  const sentGradeSub = document.getElementById('sentGradeSub');
  const elevationVal = document.getElementById('elevationVal');
  const gainSub = document.getElementById('gainSub');
  const hrVal = document.getElementById('hrVal');
  const hrSub = document.getElementById('hrSub');
  const hrIcon = document.getElementById('hrIcon');
  const hrZoneBadge = document.getElementById('hrZoneBadge');
  const hrZoneBars = document.getElementById('hrZoneBars');
  const profHrZoneBadge = document.getElementById('profHrZoneBadge');
  const cadenceVal = document.getElementById('cadenceVal');
  const gearVal = document.getElementById('gearVal');
  const distVal = document.getElementById('distVal');
  const calSub = document.getElementById('calSub');
  const throttleBar = document.getElementById('throttleBar');
  const throttleVal = document.getElementById('throttleVal');
  const brakeBar = document.getElementById('brakeBar');
  const brakeVal = document.getElementById('brakeVal');

  // ヘッダー / セッション
  const btnRecord = document.getElementById('btnRecord');
  const recText = document.getElementById('recText');
  const sessionTimer = document.getElementById('sessionTimer');
  const autoPauseBadge = document.getElementById('autoPauseBadge');
  const modeSelect = document.getElementById('modeSelect');
  const diffSlider = document.getElementById('diffSlider');
  const diffValue = document.getElementById('diffValue');
  const btnFinishSession = document.getElementById('btnFinishSession');

  // マップコントロール
  const btnFollowCar = document.getElementById('btnFollowCar');
  const btnTogglePins = document.getElementById('btnTogglePins');
  const btnCalibrate = document.getElementById('btnCalibrate');
  const calibrationBanner = document.getElementById('calibrationBanner');
  const btnCancelCalibrate = document.getElementById('btnCancelCalibrate');
  const btnResetView = document.getElementById('btnResetView');
  const btnClearTrack = document.getElementById('btnClearTrack');
  const coordDisplay = document.getElementById('coordDisplay');

  // プロファイル統計・コントロール
  const profCurEle = document.getElementById('profCurEle');
  const profSpeed = document.getElementById('profSpeed');
  const profPower = document.getElementById('profPower');
  const profHr = document.getElementById('profHr');
  const profEleGain = document.getElementById('profEleGain');
  const profEleLoss = document.getElementById('profEleLoss');
  const profDist = document.getElementById('profDist');
  const profCal = document.getElementById('profCal');
  const btnProfileRecent = document.getElementById('btnProfileRecent');
  const btnProfileAll = document.getElementById('btnProfileAll');
  const profileRecentGroup = document.getElementById('profileRecentGroup');
  const selectProfileRecentDistance = document.getElementById('selectProfileRecentDistance');
  const settingProfileRecentDistance = document.getElementById('settingProfileRecentDistance');

  // ステータスフッター
  const bleDot = document.getElementById('bleDot');
  const bleText = document.getElementById('bleText');
  const cadDot = document.getElementById('cadDot');
  const cadText = document.getElementById('cadText');
  const cadStatusContainer = document.getElementById('cadStatusContainer');
  const hrDot = document.getElementById('hrDot');
  const hrText = document.getElementById('hrText');
  const hrStatusContainer = document.getElementById('hrStatusContainer');
  const udpDot = document.getElementById('udpDot');
  const udpText = document.getElementById('udpText');
  const vjoyDot = document.getElementById('vjoyDot');
  const vjoyText = document.getElementById('vjoyText');
  const saveToast = document.getElementById('saveToast');
  const btnOpenFolder = document.getElementById('btnOpenFolder');

  // ログ
  const logConsole = document.getElementById('logConsole');
  const btnClearLogs = document.getElementById('btnClearLogs');

  // 設定モーダル
  const btnSettings = document.getElementById('btnSettings');
  const settingsModal = document.getElementById('settingsModal');
  const btnCloseSettings = document.getElementById('btnCloseSettings');
  const btnSaveSettings = document.getElementById('btnSaveSettings');
  const settingFtp = document.getElementById('settingFtp');
  const settingWeight = document.getElementById('settingWeight');
  const settingPedalBrake = document.getElementById('settingPedalBrake');
  const settingFreerideOverlay = document.getElementById('settingFreerideOverlay');
  const settingMapScale = document.getElementById('settingMapScale');
  const settingMapScaleVal = document.getElementById('settingMapScaleVal');
  const settingInvertZ = document.getElementById('settingInvertZ');
  const btnResetMapOrigin = document.getElementById('btnResetMapOrigin');

  // BLE センサー設定要素 (スマートローラー)
  const bleStatusContainer = document.getElementById('bleStatusContainer');
  const btnScanBle = document.getElementById('btnScanBle');
  const currentDeviceText = document.getElementById('currentDeviceText');
  const bleScanningIndicator = document.getElementById('bleScanningIndicator');
  const bleDeviceList = document.getElementById('bleDeviceList');
  const btnConnectBle = document.getElementById('btnConnectBle');
  const bleConnectStatus = document.getElementById('bleConnectStatus');

  // ケイデンス / パワーメーター設定要素 (Magene PES-P505 等)
  const btnScanCad = document.getElementById('btnScanCad');
  const currentCadDeviceText = document.getElementById('currentCadDeviceText');
  const btnDisconnectCad = document.getElementById('btnDisconnectCad');
  const cadScanningIndicator = document.getElementById('cadScanningIndicator');
  const cadDeviceList = document.getElementById('cadDeviceList');
  const btnConnectCad = document.getElementById('btnConnectCad');
  const cadConnectStatus = document.getElementById('cadConnectStatus');
  const settingPreferPowerMeter = document.getElementById('settingPreferPowerMeter');

  // 心拍計設定要素
  const btnScanHr = document.getElementById('btnScanHr');
  const currentHrDeviceText = document.getElementById('currentHrDeviceText');
  const btnDisconnectHr = document.getElementById('btnDisconnectHr');
  const hrScanningIndicator = document.getElementById('hrScanningIndicator');
  const hrDeviceList = document.getElementById('hrDeviceList');
  const btnConnectHr = document.getElementById('btnConnectHr');
  const hrConnectStatus = document.getElementById('hrConnectStatus');
  const settingMaxHr = document.getElementById('settingMaxHr');
  const settingRestingHr = document.getElementById('settingRestingHr');

  let currentFtp = 200;
  let currentRiderWeight = 70.0;
  let currentMaxHr = 190;
  let currentRestingHr = 60;
  let lastSavedFilePath = '';
  let selectedBleDevice = null;
  let selectedCadDevice = null;
  let selectedHrDevice = null;

  // 心拍ゾーン判定共通ヘルパー (カルボーネン法 / HRR 予備心拍数基準)
  window.getHrZone = function(hr, maxHr = currentMaxHr, restingHr = currentRestingHr) {
    if (!hr || hr <= 0) {
      return { zone: 0, pct: 0, name: '未計測', shortName: 'Z0', color: '#64748b' };
    }
    let pct = 0;
    if (restingHr > 0 && maxHr > restingHr) {
      const hrr = maxHr - restingHr;
      pct = ((hr - restingHr) / hrr) * 100;
    } else {
      pct = (hr / maxHr) * 100;
    }

    if (pct >= 90) {
      return { zone: 5, pct: Math.round(pct), name: '無酸素/最大', shortName: 'Z5', color: '#ef4444' };
    } else if (pct >= 80) {
      return { zone: 4, pct: Math.round(pct), name: '乳酸閾値', shortName: 'Z4', color: '#f97316' };
    } else if (pct >= 70) {
      return { zone: 3, pct: Math.round(pct), name: 'テンポ', shortName: 'Z3', color: '#eab308' };
    } else if (pct >= 60) {
      return { zone: 2, pct: Math.round(pct), name: '有酸素', shortName: 'Z2', color: '#22c55e' };
    } else {
      return { zone: 1, pct: Math.max(0, Math.round(pct)), name: '回復', shortName: 'Z1', color: '#38bdf8' };
    }
  };

  // 各ゾーンの心拍数範囲 (bpm) 取得ヘルパー
  window.getHrZoneRanges = function(maxHr = currentMaxHr, restingHr = currentRestingHr) {
    const calc = (pct) => {
      if (restingHr > 0 && maxHr > restingHr) {
        return Math.round(restingHr + (maxHr - restingHr) * (pct / 100));
      }
      return Math.round(maxHr * (pct / 100));
    };
    return [
      { zone: 1, name: '回復', shortName: 'Z1', min: restingHr > 0 ? restingHr : calc(50), max: calc(60) - 1, color: '#38bdf8' },
      { zone: 2, name: '有酸素', shortName: 'Z2', min: calc(60), max: calc(70) - 1, color: '#22c55e' },
      { zone: 3, name: 'テンポ', shortName: 'Z3', min: calc(70), max: calc(80) - 1, color: '#eab308' },
      { zone: 4, name: '乳酸閾値', shortName: 'Z4', min: calc(80), max: calc(90) - 1, color: '#f97316' },
      { zone: 5, name: '無酸素/最大', shortName: 'Z5', min: calc(90), max: maxHr, color: '#ef4444' }
    ];
  };

  // タブ切り替え処理
  const tabButtons = document.querySelectorAll('.tab-btn');
  const tabContents = document.querySelectorAll('.tab-content');

  tabButtons.forEach(btn => {
    btn.addEventListener('click', () => {
      const targetTabId = btn.dataset.tab;

      tabButtons.forEach(b => b.classList.remove('active'));
      tabContents.forEach(c => c.classList.remove('active'));

      btn.classList.add('active');
      const targetContent = document.getElementById(targetTabId);
      if (targetContent) {
        targetContent.classList.add('active');
      }

      // マップタブが表示されたらリサイズを通知
      if (targetTabId === 'tabMap' && mapTracker.map) {
        setTimeout(() => mapTracker.map.invalidateSize(), 50);
      }
      // 走行プロファイルタブが表示されたらリサイズと再描画
      if (targetTabId === 'tabElevation' && elevationChart.chart) {
        setTimeout(() => {
          elevationChart.chart.resize();
          elevationChart.renderChart();
        }, 50);
      }
      // 走行履歴タブが表示されたらリサイズと最新一覧の更新
      if (targetTabId === 'tabHistory') {
        const curScale = parseFloat(settingMapScale.value);
        if (curScale) {
          historyController.setMapConfig({
            mapScale: curScale,
            invertZ: settingInvertZ.checked
          });
        }
        historyController.invalidateSize();
        historyController.refreshList();
      }
    });
  });

  // C# へのメッセージ送信ヘルパー
  function postMessageToHost(action, payload = {}) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage({ action, ...payload });
    } else {
      console.log('[DevWebMessage]', action, payload);
    }
  }

  // UI イベントリスナー
  modeSelect.addEventListener('change', (e) => {
    postMessageToHost('setMode', { mode: parseInt(e.target.value) });
  });

  diffSlider.addEventListener('input', (e) => {
    const val = parseInt(e.target.value);
    diffValue.textContent = `${val}%`;
    postMessageToHost('setDifficulty', { difficulty: val / 100.0 });
  });

  btnRecord.addEventListener('click', () => {
    const isRecording = btnRecord.classList.contains('recording');
    if (!isRecording) {
      postMessageToHost('startSession');
    } else {
      postMessageToHost('pauseSession');
    }
  });

  btnFinishSession.addEventListener('click', () => {
    if (confirm('現在の走行セッションを終了し、GPXログを保存しますか？')) {
      postMessageToHost('stopSession');
    }
  });

  // 走行プロファイル表示切り替え（直近 / 全走行距離）
  if (profileRecentGroup && btnProfileAll) {
    profileRecentGroup.addEventListener('click', (e) => {
      if (e.target === selectProfileRecentDistance) return;
      profileRecentGroup.classList.add('active');
      if (btnProfileRecent) btnProfileRecent.classList.add('active');
      btnProfileAll.classList.remove('active');
      elevationChart.setViewMode('recent');
    });

    btnProfileAll.addEventListener('click', () => {
      profileRecentGroup.classList.remove('active');
      if (btnProfileRecent) btnProfileRecent.classList.remove('active');
      btnProfileAll.classList.add('active');
      elevationChart.setViewMode('all');
    });
  }

  // 直近距離セレクト変更 (100m〜1000m)
  if (selectProfileRecentDistance) {
    selectProfileRecentDistance.addEventListener('change', (e) => {
      const dist = parseInt(e.target.value) || 200;
      elevationChart.setRecentDistance(dist);
      elevationChart.setViewMode('recent');
      if (profileRecentGroup) profileRecentGroup.classList.add('active');
      if (btnProfileRecent) btnProfileRecent.classList.add('active');
      if (btnProfileAll) btnProfileAll.classList.remove('active');
      if (settingProfileRecentDistance) settingProfileRecentDistance.value = dist.toString();
      postMessageToHost('setProfileRecentDistance', { distance: dist });
    });
  }

  btnFollowCar.addEventListener('click', () => {
    mapTracker.followCar = !mapTracker.followCar;
    btnFollowCar.classList.toggle('active', mapTracker.followCar);
    btnFollowCar.textContent = mapTracker.followCar ? '📍 自車追従: ON' : '📍 自車追従: OFF';
  });

  btnTogglePins.addEventListener('click', () => {
    const isVisible = mapTracker.togglePins();
    btnTogglePins.classList.toggle('active', isVisible);
    btnTogglePins.textContent = isVisible ? '📌 スポット: ON' : '📌 スポット: OFF';
  });

  // キャリブレーション（位置合わせモード）
  function endCalibration() {
    btnCalibrate.classList.remove('calibrating');
    calibrationBanner.classList.add('hidden');
    mapTracker.setCalibrationMode(false, null);
  }

  btnCalibrate.addEventListener('click', () => {
    const isCalibrating = btnCalibrate.classList.contains('calibrating');
    if (isCalibrating) {
      endCalibration();
    } else {
      btnCalibrate.classList.add('calibrating');
      calibrationBanner.classList.remove('hidden');
      mapTracker.setCalibrationMode(true, (px, py) => {
        postMessageToHost('calibrateMap', { pixelX: px, pixelY: py });
        appendLog(`[MAP] 🎯 位置合わせ実行: 地図位置 (${Math.round(px)}, ${Math.round(py)}) に自車を吸着しました`);
        endCalibration();
      });
    }
  });

  btnCancelCalibrate.addEventListener('click', () => {
    endCalibration();
  });

  btnResetView.addEventListener('click', () => {
    mapTracker.resetView();
  });

  btnClearTrack.addEventListener('click', () => {
    mapTracker.clearTrack();
    elevationChart.reset();
    gainSub.textContent = '獲得標高: +0 m';
    if (profEleGain) profEleGain.textContent = '+0 m';
    if (profDist) profDist.textContent = '0.00 km';
    postMessageToHost('clearTrack');
    appendLog('[TRACK] 🧹 軌跡をクリアし、前セッションを保存して新しい記録を開始しました');
  });

  btnClearLogs.addEventListener('click', () => {
    logConsole.innerHTML = '';
  });

  // 設定モーダル
  btnSettings.addEventListener('click', () => {
    settingsModal.classList.remove('hidden');
  });

  btnCloseSettings.addEventListener('click', () => {
    settingsModal.classList.add('hidden');
  });

  // スライダー変更時の数値表示更新
  function updatePresetButtons(scaleVal) {
    document.querySelectorAll('.btn-preset').forEach(btn => {
      const pScale = parseFloat(btn.dataset.scale);
      btn.classList.toggle('active', Math.abs(pScale - scaleVal) < 0.0015);
    });
  }

  settingMapScale.addEventListener('input', (e) => {
    const val = parseFloat(e.target.value);
    settingMapScaleVal.textContent = val.toFixed(3);
    updatePresetButtons(val);
  });

  document.querySelectorAll('.btn-preset').forEach(btn => {
    btn.addEventListener('click', () => {
      const pScale = parseFloat(btn.dataset.scale);
      settingMapScale.value = pScale;
      settingMapScaleVal.textContent = pScale.toFixed(3);
      updatePresetButtons(pScale);
    });
  });

  // 原点リセットボタン
  btnResetMapOrigin.addEventListener('click', () => {
    if (confirm('マップ原点を初期位置 (X=0, Z=0) にリセットしますか？')) {
      postMessageToHost('updateMapConfig', {
        scale: 0.263,
        invertZ: true,
        originX: 0.0,
        originZ: 0.0
      });
      settingMapScale.value = 0.263;
      settingMapScaleVal.textContent = '0.263';
      updatePresetButtons(0.263);
      settingInvertZ.checked = true;
      appendLog('[MAP] 原点を初期位置 (0.0, 0.0, 縮尺 0.263) にリセットしました');
    }
  });

  // フッターのBLEステータスをクリックしたときも設定を開く
  if (bleStatusContainer) {
    bleStatusContainer.addEventListener('click', () => {
      settingsModal.classList.remove('hidden');
    });
  }
  if (hrStatusContainer) {
    hrStatusContainer.addEventListener('click', () => {
      settingsModal.classList.remove('hidden');
    });
  }

  // BLE デバイススキャン開始 (トレーナー用)
  btnScanBle.addEventListener('click', () => {
    bleScanningIndicator.classList.remove('hidden');
    btnScanBle.disabled = true;
    btnConnectBle.disabled = true;
    bleConnectStatus.textContent = '';
    bleDeviceList.innerHTML = '<div class="device-list-empty">周囲のスマートローラー・パワーメーターを検索中...</div>';
    selectedBleDevice = null;
    postMessageToHost('scanBle');
  });

  // 選択デバイスへの接続 (トレーナー用)
  btnConnectBle.addEventListener('click', () => {
    if (!selectedBleDevice) return;
    btnConnectBle.disabled = true;
    bleConnectStatus.textContent = `接続中: ${selectedBleDevice.Name}...`;
    postMessageToHost('connectSensor', {
      type: selectedBleDevice.SensorTypeInt,
      mac: selectedBleDevice.Address,
      name: selectedBleDevice.Name
    });
  });

  // フッターのケイデンスインジケータークリックで設定を開く
  if (cadStatusContainer) {
    cadStatusContainer.addEventListener('click', () => {
      settingsModal.classList.remove('hidden');
    });
  }

  // ケイデンス / パワーメーター スキャン開始
  if (btnScanCad) {
    btnScanCad.addEventListener('click', () => {
      if (cadScanningIndicator) cadScanningIndicator.classList.remove('hidden');
      btnScanCad.disabled = true;
      if (btnConnectCad) btnConnectCad.disabled = true;
      if (cadConnectStatus) cadConnectStatus.textContent = '';
      if (cadDeviceList) cadDeviceList.innerHTML = '<div class="device-list-empty">周囲のケイデンス/パワーメーターを検索中...</div>';
      selectedCadDevice = null;
      postMessageToHost('scanBle');
    });
  }

  // 選択したケイデンス / パワーメーターへの接続
  if (btnConnectCad) {
    btnConnectCad.addEventListener('click', () => {
      if (!selectedCadDevice) return;
      btnConnectCad.disabled = true;
      if (cadConnectStatus) cadConnectStatus.textContent = `接続中: ${selectedCadDevice.Name}...`;
      postMessageToHost('connectCadence', {
        mac: selectedCadDevice.Address,
        name: selectedCadDevice.Name
      });
    });
  }

  // ケイデンス / パワーメーター切断
  if (btnDisconnectCad) {
    btnDisconnectCad.addEventListener('click', () => {
      postMessageToHost('disconnectCadence');
      if (currentCadDeviceText) currentCadDeviceText.textContent = 'None';
      btnDisconnectCad.classList.add('hidden');
      if (cadConnectStatus) cadConnectStatus.textContent = 'センサーを切断しました。';
      if (btnConnectCad) btnConnectCad.disabled = true;

      if (cadDot) cadDot.className = 'status-dot disconnected';
      if (cadText) cadText.textContent = 'CAD: 切断';
    });
  }

  // パワー値優先チェックボックス
  if (settingPreferPowerMeter) {
    settingPreferPowerMeter.addEventListener('change', (e) => {
      postMessageToHost('setPreferPowerMeterWatts', { prefer: e.target.checked });
    });
  }

  // 心拍計スキャン開始
  if (btnScanHr) {
    btnScanHr.addEventListener('click', () => {
      hrScanningIndicator.classList.remove('hidden');
      btnScanHr.disabled = true;
      btnConnectHr.disabled = true;
      hrConnectStatus.textContent = '';
      hrDeviceList.innerHTML = '<div class="device-list-empty">周囲のBluetooth心拍計を検索中...</div>';
      selectedHrDevice = null;
      postMessageToHost('scanBle');
    });
  }

  // 選択した心拍計への接続
  if (btnConnectHr) {
    btnConnectHr.addEventListener('click', () => {
      if (!selectedHrDevice) return;
      btnConnectHr.disabled = true;
      hrConnectStatus.textContent = `接続中: ${selectedHrDevice.Name}...`;
      postMessageToHost('connectHeartRate', {
        mac: selectedHrDevice.Address,
        name: selectedHrDevice.Name
      });
    });
  }

  // 心拍計切断
  if (btnDisconnectHr) {
    btnDisconnectHr.addEventListener('click', () => {
      postMessageToHost('disconnectHeartRate');
      currentHrDeviceText.textContent = 'None';
      btnDisconnectHr.classList.add('hidden');
      hrConnectStatus.textContent = '心拍計を切断しました。';
      btnConnectHr.disabled = true;

      // フッター及びメインHUDも即時更新
      if (hrDot) hrDot.className = 'status-dot disconnected';
      if (hrText) hrText.textContent = 'HR: 切断';
      if (hrVal) hrVal.textContent = '--';
      if (hrSub) hrSub.textContent = '未接続';
      if (hrIcon) hrIcon.classList.remove('beating');
    });
  }

  btnSaveSettings.addEventListener('click', () => {
    const ftp = parseInt(settingFtp.value) || 200;
    const weight = parseFloat(settingWeight ? settingWeight.value : 70.0) || 70.0;
    const maxHr = parseInt(settingMaxHr ? settingMaxHr.value : 190) || 190;
    const restingHr = parseInt(settingRestingHr ? settingRestingHr.value : 60) || 60;
    const pedalBrake = settingPedalBrake.checked;
    const freerideOverlay = settingFreerideOverlay ? settingFreerideOverlay.checked : false;
    const recentDist = parseInt(settingProfileRecentDistance ? settingProfileRecentDistance.value : 200) || 200;
    currentFtp = ftp;
    currentRiderWeight = weight;
    currentMaxHr = maxHr;
    currentRestingHr = restingHr;
    elevationChart.setRecentDistance(recentDist);
    if (elevationChart.setHrConfig) elevationChart.setHrConfig(maxHr, restingHr);
    if (window.historyController && window.historyController.setHrConfig) {
      window.historyController.setHrConfig(maxHr, restingHr);
    }
    if (selectProfileRecentDistance) selectProfileRecentDistance.value = recentDist.toString();
    postMessageToHost('updateSettings', {
      ftp,
      weight,
      pedalBrake,
      freerideOverlay,
      maxHeartRate: maxHr,
      restingHeartRate: restingHr,
      profileRecentDistance: recentDist
    });

    // マップ設定も保存
    const mapScale = parseFloat(settingMapScale.value) || 0.70;
    const invertZ = settingInvertZ.checked;
    postMessageToHost('updateMapConfig', { scale: mapScale, invertZ: invertZ });

    settingsModal.classList.add('hidden');
  });

  btnOpenFolder.addEventListener('click', (e) => {
    e.preventDefault();
    postMessageToHost('openLogFolder', { path: lastSavedFilePath });
  });

  // 秒数を HH:MM:SS にフォーマット
  function formatSeconds(totalSec) {
    const s = Math.floor(totalSec);
    const hrs = Math.floor(s / 3600).toString().padStart(2, '0');
    const mins = Math.floor((s % 3600) / 60).toString().padStart(2, '0');
    const secs = (s % 60).toString().padStart(2, '0');
    return `${hrs}:${mins}:${secs}`;
  }

  // ログ出力ヘルパー
  function appendLog(message) {
    const line = document.createElement('div');
    line.className = 'log-line';
    const timestamp = new Date().toLocaleTimeString();
    line.textContent = `[${timestamp}] ${message}`;
    logConsole.appendChild(line);
    logConsole.scrollTop = logConsole.scrollHeight;
  }

  // C# (WebView2) からのメッセージ受信ハンドラ
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', (event) => {
      const data = event.data;
      if (!data) return;

      if (data.type === 'state') {
        handleStateUpdate(data.payload);
      } else if (data.type === 'log') {
        appendLog(data.payload);
      } else if (data.type === 'sessionSaved') {
        handleSessionSaved(data.payload);
      } else if (data.type === 'historyList') {
        historyController.handleHistoryList(data.payload);
      } else if (data.type === 'historyDetail') {
        historyController.handleHistoryDetail(data.payload, data);
      } else if (data.type === 'historyDeleted') {
        historyController.handleHistoryDeleted(data.fileName, data.success);
      } else if (data.type === 'initPins') {
        mapTracker.setPins(data.payload);
      } else if (data.type === 'bleScanResult') {
        renderBleDevices(data.payload);
      } else if (data.type === 'bleConnectResult') {
        if (data.payload.success) {
          bleConnectStatus.textContent = `✅ 接続完了: ${data.payload.name}`;
          currentDeviceText.textContent = data.payload.name;
          bleDot.className = 'status-dot connected';
          bleText.textContent = `BLE: Connected (${data.payload.name})`;
        } else {
          bleConnectStatus.textContent = `❌ 接続失敗: ${data.payload.name}`;
          btnConnectBle.disabled = false;
        }
      } else if (data.type === 'cadenceConnectResult') {
        if (data.payload.success) {
          if (cadConnectStatus) cadConnectStatus.textContent = `✅ 接続完了: ${data.payload.name}`;
          if (currentCadDeviceText) currentCadDeviceText.textContent = data.payload.name;
          if (btnDisconnectCad) btnDisconnectCad.classList.remove('hidden');
          if (cadDot) cadDot.className = 'status-dot connected';
          if (cadText) cadText.textContent = `CAD: Connected (${data.payload.name})`;
        } else {
          if (cadConnectStatus) cadConnectStatus.textContent = `❌ 接続失敗: ${data.payload.name}`;
          if (btnConnectCad) btnConnectCad.disabled = false;
        }
      } else if (data.type === 'hrConnectResult') {
        if (data.payload.success) {
          hrConnectStatus.textContent = `✅ 接続完了: ${data.payload.name}`;
          currentHrDeviceText.textContent = data.payload.name;
          if (btnDisconnectHr) btnDisconnectHr.classList.remove('hidden');
          if (hrDot) hrDot.className = 'status-dot connected';
          if (hrText) hrText.textContent = `HR: Connected (${data.payload.name})`;
          if (hrSub) hrSub.textContent = '0 bpm';
        } else {
          hrConnectStatus.textContent = `❌ 接続失敗: ${data.payload.name}`;
          btnConnectHr.disabled = false;
          if (hrDot) hrDot.className = 'status-dot disconnected';
          if (hrText) hrText.textContent = 'HR: 切断';
          if (hrSub) hrSub.textContent = '未接続';
        }
      } else if (data.type === 'config') {
        historyController.setMapConfig(data.payload);
        if (data.payload.defaultMode) {
          modeSelect.value = data.payload.defaultMode.toString();
        }
        if (typeof data.payload.difficulty !== 'undefined') {
          const diffPct = Math.round(data.payload.difficulty * 100);
          diffSlider.value = diffPct;
          diffValue.textContent = `${diffPct}%`;
        }
        if (data.payload.ftp) {
          currentFtp = data.payload.ftp;
          settingFtp.value = currentFtp;
        }
        if (typeof data.payload.weight !== 'undefined') {
          currentRiderWeight = data.payload.weight;
          if (settingWeight) {
            settingWeight.value = currentRiderWeight;
          }
        }
        if (typeof data.payload.pedalBrake !== 'undefined' && settingPedalBrake) {
          settingPedalBrake.checked = data.payload.pedalBrake;
        }
        if (typeof data.payload.freerideOverlay !== 'undefined' && settingFreerideOverlay) {
          settingFreerideOverlay.checked = data.payload.freerideOverlay;
        }
        if (data.payload.maxHeartRate && settingMaxHr) {
          currentMaxHr = data.payload.maxHeartRate;
          settingMaxHr.value = currentMaxHr;
        }
        if (data.payload.restingHeartRate && settingRestingHr) {
          currentRestingHr = data.payload.restingHeartRate;
          settingRestingHr.value = currentRestingHr;
        }
        if (elevationChart.setHrConfig) {
          elevationChart.setHrConfig(currentMaxHr, currentRestingHr);
        }
        if (window.historyController && window.historyController.setHrConfig) {
          window.historyController.setHrConfig(currentMaxHr, currentRestingHr);
        }
        if (data.payload.powerSourceName && currentDeviceText) {
          currentDeviceText.textContent = data.payload.powerSourceName;
        }
        if (data.payload.cadenceSourceName && currentCadDeviceText) {
          currentCadDeviceText.textContent = data.payload.cadenceSourceName;
          if (btnDisconnectCad) btnDisconnectCad.classList.remove('hidden');
        }
        if (typeof data.payload.preferPowerMeterWatts !== 'undefined' && settingPreferPowerMeter) {
          settingPreferPowerMeter.checked = data.payload.preferPowerMeterWatts;
        }
        if (data.payload.heartRateSourceName && currentHrDeviceText) {
          currentHrDeviceText.textContent = data.payload.heartRateSourceName;
          if (btnDisconnectHr) btnDisconnectHr.classList.remove('hidden');
        }
        if (typeof data.payload.pedalBrake !== 'undefined') {
          settingPedalBrake.checked = data.payload.pedalBrake;
        }
        if (data.payload.profileRecentDistance) {
          const dist = data.payload.profileRecentDistance;
          elevationChart.setRecentDistance(dist);
          if (selectProfileRecentDistance) selectProfileRecentDistance.value = dist.toString();
          if (settingProfileRecentDistance) settingProfileRecentDistance.value = dist.toString();
        }
        if (data.payload.mapScale) {
          settingMapScale.value = data.payload.mapScale;
          settingMapScaleVal.textContent = parseFloat(data.payload.mapScale).toFixed(3);
          updatePresetButtons(parseFloat(data.payload.mapScale));
        }
        if (typeof data.payload.invertZ !== 'undefined') {
          settingInvertZ.checked = data.payload.invertZ;
        }
      }
    });
  }

  // BLEデバイス一覧の描画 (トレーナー、ケイデンス/パワーメーター、心拍計に分別)
  function renderBleDevices(devices) {
    bleScanningIndicator.classList.add('hidden');
    if (cadScanningIndicator) cadScanningIndicator.classList.add('hidden');
    if (hrScanningIndicator) hrScanningIndicator.classList.add('hidden');
    btnScanBle.disabled = false;
    if (btnScanCad) btnScanCad.disabled = false;
    if (btnScanHr) btnScanHr.disabled = false;

    if (!devices || devices.length === 0) {
      bleDeviceList.innerHTML = '<div class="device-list-empty">デバイスが見つかりませんでした。電源やBluetoothがONになっているか確認してください。</div>';
      if (cadDeviceList) cadDeviceList.innerHTML = '<div class="device-list-empty">ケイデンス/パワーメーターが見つかりませんでした。</div>';
      if (hrDeviceList) hrDeviceList.innerHTML = '<div class="device-list-empty">心拍計が見つかりませんでした。</div>';
      return;
    }

    const trainerDevices = devices.filter(d => d.Category === 'trainer' || d.SensorTypeInt === 1 || d.SensorTypeInt === 2 || d.Category === 'other');
    const cadenceDevices = devices.filter(d => d.Category === 'cadence' || d.SensorTypeInt === 2 || d.SensorTypeInt === 1 || d.Category === 'other');
    const hrDevices = devices.filter(d => d.Category === 'heartrate' || d.SensorTypeInt === 3 || d.Category === 'other');

    // 1. トレーナー一覧描画
    if (trainerDevices.length === 0) {
      bleDeviceList.innerHTML = '<div class="device-list-empty">スマートローラーが見つかりませんでした。</div>';
    } else {
      bleDeviceList.innerHTML = '';
      trainerDevices.forEach(dev => {
        const item = document.createElement('div');
        item.className = 'device-item';
        const isPower = dev.SensorTypeInt === 2;
        const isOther = dev.Category === 'other';
        const badgeText = isOther ? 'Bluetooth' : dev.TypeName;
        const badgeClass = isPower ? 'power' : '';
        item.innerHTML = `
          <div class="device-item-left">
            <span class="device-item-name">🚴 ${dev.Name}</span>
            <span class="device-item-sub">MAC: ${dev.FormattedMac} ${dev.IsPaired ? '[ペアリング済]' : ''}</span>
          </div>
          <span class="device-item-badge ${badgeClass}">${badgeText}</span>
        `;
        item.addEventListener('click', () => {
          bleDeviceList.querySelectorAll('.device-item').forEach(el => el.classList.remove('selected'));
          item.classList.add('selected');
          selectedBleDevice = dev;
          btnConnectBle.disabled = false;
          bleConnectStatus.textContent = `選択中: ${dev.Name}`;
        });
        bleDeviceList.appendChild(item);
      });
    }

    // 2. ケイデンス / パワーメーター一覧描画 (Magene PES-P505 等)
    if (cadDeviceList) {
      if (cadenceDevices.length === 0) {
        cadDeviceList.innerHTML = '<div class="device-list-empty">ケイデンス/パワーメーターが見つかりませんでした。</div>';
      } else {
        cadDeviceList.innerHTML = '';
        cadenceDevices.forEach(dev => {
          const item = document.createElement('div');
          item.className = 'device-item';
          const isMageneOrP505 = dev.Name.toUpperCase().includes('PES') || dev.Name.toUpperCase().includes('P505') || dev.Name.toUpperCase().includes('MAGENE');
          const badgeText = isMageneOrP505 ? 'P505 / Power' : dev.TypeName;
          item.innerHTML = `
            <div class="device-item-left">
              <span class="device-item-name">⚡ ${dev.Name}</span>
              <span class="device-item-sub">MAC: ${dev.FormattedMac} ${dev.IsPaired ? '[ペアリング済]' : ''}</span>
            </div>
            <span class="device-item-badge power">${badgeText}</span>
          `;
          item.addEventListener('click', () => {
            cadDeviceList.querySelectorAll('.device-item').forEach(el => el.classList.remove('selected'));
            item.classList.add('selected');
            selectedCadDevice = dev;
            if (btnConnectCad) btnConnectCad.disabled = false;
            if (cadConnectStatus) cadConnectStatus.textContent = `選択中: ${dev.Name}`;
          });
          cadDeviceList.appendChild(item);
        });
      }
    }

    // 3. 心拍計一覧描画
    if (hrDeviceList) {
      if (hrDevices.length === 0) {
        hrDeviceList.innerHTML = '<div class="device-list-empty">周囲にBluetooth心拍計が見つかりませんでした。</div>';
      } else {
        hrDeviceList.innerHTML = '';
        hrDevices.forEach(dev => {
          const item = document.createElement('div');
          item.className = 'device-item';
          const isKnownHr = dev.Category === 'heartrate' || dev.SensorTypeInt === 3;
          const iconPrefix = isKnownHr ? '❤️ ' : '🔍 ';
          const badgeStyle = isKnownHr 
            ? 'background: rgba(244, 63, 94, 0.2); color: #f43f5e; border-color: rgba(244, 63, 94, 0.4);'
            : 'background: rgba(148, 163, 184, 0.2); color: #cbd5e1; border-color: rgba(148, 163, 184, 0.4);';
          const badgeText = isKnownHr ? 'Heart Rate' : 'Bluetooth';
          item.innerHTML = `
            <div class="device-item-left">
              <span class="device-item-name">${iconPrefix}${dev.Name}</span>
              <span class="device-item-sub">MAC: ${dev.FormattedMac} ${dev.IsPaired ? '[ペアリング済]' : ''}</span>
            </div>
            <span class="device-item-badge" style="${badgeStyle}">${badgeText}</span>
          `;
          item.addEventListener('click', () => {
            hrDeviceList.querySelectorAll('.device-item').forEach(el => el.classList.remove('selected'));
            item.classList.add('selected');
            selectedHrDevice = dev;
            btnConnectHr.disabled = false;
            hrConnectStatus.textContent = `選択中: ${dev.Name}`;
          });
          hrDeviceList.appendChild(item);
        });
      }
    }
  }

  // 状態スナップショットのUI反映
  function handleStateUpdate(s) {
    // 1. パワー
    if (s.Ftp && s.Ftp > 0) {
      currentFtp = s.Ftp;
    }
    if (s.RiderWeightKg && s.RiderWeightKg > 0) {
      currentRiderWeight = s.RiderWeightKg;
    }
    const power = Math.round(s.Power);
    powerVal.textContent = power;
    const wkg = (s.Wkg && s.Wkg > 0) ? s.Wkg.toFixed(1) : (power / currentRiderWeight).toFixed(1);
    wkgVal.textContent = `${wkg} W/kg`;
    const zonePercent = Math.min(100, (power / (currentFtp * 1.5)) * 100);
    powerZoneIndicator.style.width = `${zonePercent}%`;

    // 2. 速度
    carSpeedVal.textContent = s.CarSpeedKmh.toFixed(1);
    targetSpeedVal.textContent = s.TargetSpeedKmh.toFixed(1);

    // 3. 勾配・標高・心拍
    const gradeSign = s.RawGrade >= 0 ? '+' : '';
    gradeVal.textContent = `${gradeSign}${s.RawGrade.toFixed(1)}`;
    sentGradeSub.textContent = `送信: ${s.SentGrade.toFixed(1)}%`;

    elevationVal.textContent = Math.round(s.ElevationMeters);
    gainSub.textContent = `獲得: +${Math.round(s.ElevationGainMeters)} m`;

    if (hrVal) {
      if (s.HeartRate && s.HeartRate > 0) {
        hrVal.textContent = Math.round(s.HeartRate);
        if (hrIcon) hrIcon.classList.add('beating');

        const zInfo = window.getHrZone(s.HeartRate, currentMaxHr, currentRestingHr);
        if (hrZoneBadge) {
          hrZoneBadge.className = `hr-zone-badge zone-${zInfo.zone}`;
          hrZoneBadge.textContent = `${zInfo.shortName} ${zInfo.name}`;
        }
        if (hrZoneBars) {
          const segments = hrZoneBars.querySelectorAll('.hr-zone-segment');
          segments.forEach(seg => {
            const zNum = parseInt(seg.getAttribute('data-zone')) || 0;
            seg.classList.toggle('active', zNum <= zInfo.zone);
          });
        }
        if (hrSub) {
          hrSub.textContent = `${zInfo.pct}% HRR (${currentRestingHr}-${currentMaxHr}bpm)`;
        }
      } else {
        hrVal.textContent = '--';
        if (hrIcon) hrIcon.classList.remove('beating');
        if (hrZoneBadge) {
          hrZoneBadge.className = 'hr-zone-badge';
          hrZoneBadge.textContent = 'Z0 --';
        }
        if (hrZoneBars) {
          const segments = hrZoneBars.querySelectorAll('.hr-zone-segment');
          segments.forEach(seg => seg.classList.remove('active'));
        }
        if (hrSub) {
          hrSub.textContent = s.IsHrConnected ? '0 bpm' : '未接続';
        }
      }
    }

    // 4. ケイデンス・ギア・距離・カロリー
    cadenceVal.textContent = s.Cadence > 0 ? Math.round(s.Cadence) : '--';
    gearVal.textContent = s.IsTelemetryActive ? (s.Gear === 0 ? 'R' : `${s.Gear}`) : '--';
    distVal.textContent = s.TotalDistanceKm.toFixed(2);
    if (calSub) {
      calSub.textContent = `${Math.round(s.CaloriesKcal || 0)} kcal`;
    }

    // 5. スロットル・ブレーキ
    const throttlePct = Math.round(s.Throttle * 100);
    const brakePct = Math.round(s.Brake * 100);
    throttleVal.textContent = `${throttlePct}%`;
    throttleBar.style.width = `${throttlePct}%`;
    brakeVal.textContent = `${brakePct}%`;
    brakeBar.style.width = `${brakePct}%`;

    // 6. ヘッダー / セッション
    sessionTimer.textContent = formatSeconds(s.ElapsedSeconds);
    if (s.SessionState === 'AutoPaused') {
      if (autoPauseBadge) autoPauseBadge.classList.remove('hidden');
      btnRecord.classList.add('recording');
      recText.textContent = 'REC';
    } else if (s.SessionState === 'Recording') {
      if (autoPauseBadge) autoPauseBadge.classList.add('hidden');
      btnRecord.classList.add('recording');
      recText.textContent = 'REC';
    } else if (s.SessionState === 'Paused') {
      if (autoPauseBadge) autoPauseBadge.classList.add('hidden');
      btnRecord.classList.remove('recording');
      recText.textContent = 'RESUME';
    } else {
      if (autoPauseBadge) autoPauseBadge.classList.add('hidden');
      btnRecord.classList.remove('recording');
      recText.textContent = 'REC';
    }

    // 7. マップ更新
    mapTracker.updateCar(s.MapPixelX, s.MapPixelY, s.Yaw, s.PositionX, s.PositionZ, s.IsPositionValid, s.IsTeleport);
    coordDisplay.textContent = `World: (X:${s.PositionX.toFixed(0)}, Y:${s.PositionY.toFixed(0)}, Z:${s.PositionZ.toFixed(0)}) | Map: (${Math.round(s.MapPixelX)}, ${Math.round(s.MapPixelY)})`;

    // 8. 走行プロファイル更新 (標高・速度・目標速度・パワー・心拍)
    if (profCurEle) profCurEle.textContent = `${Math.round(s.ElevationMeters)} m`;
    if (profSpeed) profSpeed.innerHTML = `${s.CarSpeedKmh.toFixed(1)} / ${s.TargetSpeedKmh.toFixed(1)} <span style="font-size:12px; font-weight:normal; color:var(--text-dim);">km/h</span>`;
    if (profPower) profPower.innerHTML = `${Math.round(s.Power)} <span style="font-size:12px; font-weight:normal; color:var(--text-dim);">W</span>`;
    if (profHr) {
      if (s.HeartRate > 0) {
        profHr.innerHTML = `${Math.round(s.HeartRate)} <span style="font-size:12px; font-weight:normal; color:var(--text-dim);">bpm</span>`;
        if (profHrZoneBadge) {
          const zInfo = window.getHrZone(s.HeartRate, currentMaxHr, currentRestingHr);
          profHrZoneBadge.className = `hr-zone-badge hr-zone-badge-mini zone-${zInfo.zone}`;
          profHrZoneBadge.textContent = zInfo.shortName;
          profHrZoneBadge.classList.remove('hidden');
        }
      } else {
        profHr.innerHTML = `-- <span style="font-size:12px; font-weight:normal; color:var(--text-dim);">bpm</span>`;
        if (profHrZoneBadge) profHrZoneBadge.classList.add('hidden');
      }
    }
    if (profEleGain) profEleGain.textContent = `+${Math.round(s.ElevationGainMeters)} m`;
    if (profDist) profDist.textContent = `${s.TotalDistanceKm.toFixed(2)} km`;
    if (profCal) profCal.textContent = `${Math.round(s.CaloriesKcal || 0)} kcal`;
    elevationChart.update(s.ElevationMeters, s.TotalDistanceKm, s.CarSpeedKmh, s.TargetSpeedKmh, s.Power, s.HeartRate);

    // 9. ステータスインジケーター
    bleDot.className = `status-dot ${s.IsBleConnected ? 'connected' : 'disconnected'}`;
    bleText.textContent = `BLE: ${s.BleStatus}`;
    if (s.ConnectedDeviceName && s.ConnectedDeviceName !== 'None') {
      currentDeviceText.textContent = s.ConnectedDeviceName;
    }

    if (hrDot) {
      hrDot.className = `status-dot ${s.IsHrConnected ? 'connected' : 'disconnected'}`;
    }
    if (hrText) {
      hrText.textContent = `HR: ${s.HrStatus || (s.IsHrConnected ? '接続' : '切断')}`;
    }
    if (s.ConnectedHrDeviceName && s.ConnectedHrDeviceName !== 'None' && currentHrDeviceText) {
      currentHrDeviceText.textContent = s.ConnectedHrDeviceName;
      if (btnDisconnectHr) btnDisconnectHr.classList.remove('hidden');
    }

    if (cadDot) {
      cadDot.className = `status-dot ${s.IsCadenceConnected ? 'connected' : 'disconnected'}`;
    }
    if (cadText) {
      cadText.textContent = `CAD: ${s.CadenceStatus || (s.IsCadenceConnected ? '接続' : '切断')}`;
    }
    if (s.ConnectedCadenceDeviceName && s.ConnectedCadenceDeviceName !== 'None' && currentCadDeviceText) {
      currentCadDeviceText.textContent = s.ConnectedCadenceDeviceName;
      if (btnDisconnectCad) btnDisconnectCad.classList.remove('hidden');
    }

    udpDot.className = `status-dot ${s.IsTelemetryActive ? 'connected' : 'disconnected'}`;
    udpText.textContent = `Forza UDP: ${s.IsTelemetryActive ? '60Hz 受信中' : '待機中'}`;

    vjoyDot.className = `status-dot ${s.IsVJoyActive ? 'connected' : 'disconnected'}`;
    vjoyText.textContent = `vJoy: ${s.IsVJoyActive ? 'Active' : '未検出'}`;
  }

  function handleSessionSaved(summary) {
    if (summary && summary.SavedFilePath) {
      lastSavedFilePath = summary.SavedFilePath;
      saveToast.innerHTML = `<span>💾 走行ログ保存完了: </span><a id="btnOpenFolderDynamic" href="#">[フォルダを開く]</a>`;
      const btnOpen = saveToast.querySelector('#btnOpenFolderDynamic');
      if (btnOpen) {
        btnOpen.addEventListener('click', (e) => {
          e.preventDefault();
          postMessageToHost('openLogFolder', { path: lastSavedFilePath });
        });
      }
      saveToast.classList.remove('hidden');
      setTimeout(() => saveToast.classList.add('hidden'), 6000);
      appendLog(`[SESSION] 走行ログ保存完了: ${summary.SavedFilePath}`);
    } else {
      saveToast.innerHTML = `<span>⚠️ 走行データがなかったため保存をスキップしました</span>`;
      saveToast.classList.remove('hidden');
      setTimeout(() => saveToast.classList.add('hidden'), 4000);
      appendLog('[SESSION] 走行データがないため保存をスキップしました');
    }
    if (historyController) {
      historyController.refreshList();
    }
  }

  // 初期化完了をC#ホストに通知
  postMessageToHost('ready');
});
