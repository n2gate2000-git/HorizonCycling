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
  const cadenceVal = document.getElementById('cadenceVal');
  const gearVal = document.getElementById('gearVal');
  const distVal = document.getElementById('distVal');
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
  const profEleGain = document.getElementById('profEleGain');
  const profEleLoss = document.getElementById('profEleLoss');
  const profDist = document.getElementById('profDist');
  const btnProfileRecent = document.getElementById('btnProfileRecent');
  const btnProfileAll = document.getElementById('btnProfileAll');

  // ステータスフッター
  const bleDot = document.getElementById('bleDot');
  const bleText = document.getElementById('bleText');
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
  const settingPedalBrake = document.getElementById('settingPedalBrake');
  const settingMapScale = document.getElementById('settingMapScale');
  const settingMapScaleVal = document.getElementById('settingMapScaleVal');
  const settingInvertZ = document.getElementById('settingInvertZ');
  const btnResetMapOrigin = document.getElementById('btnResetMapOrigin');

  // BLE センサー設定要素
  const bleStatusContainer = document.getElementById('bleStatusContainer');
  const btnScanBle = document.getElementById('btnScanBle');
  const currentDeviceText = document.getElementById('currentDeviceText');
  const bleScanningIndicator = document.getElementById('bleScanningIndicator');
  const bleDeviceList = document.getElementById('bleDeviceList');
  const btnConnectBle = document.getElementById('btnConnectBle');
  const bleConnectStatus = document.getElementById('bleConnectStatus');

  let currentFtp = 200;
  let lastSavedFilePath = '';
  let selectedBleDevice = null;

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

  // 走行プロファイル表示切り替え（直近200m / 全走行距離）
  if (btnProfileRecent && btnProfileAll) {
    btnProfileRecent.addEventListener('click', () => {
      btnProfileRecent.classList.add('active');
      btnProfileAll.classList.remove('active');
      elevationChart.setViewMode('recent');
    });

    btnProfileAll.addEventListener('click', () => {
      btnProfileAll.classList.add('active');
      btnProfileRecent.classList.remove('active');
      elevationChart.setViewMode('all');
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

  // BLE デバイススキャン開始
  btnScanBle.addEventListener('click', () => {
    bleScanningIndicator.classList.remove('hidden');
    btnScanBle.disabled = true;
    btnConnectBle.disabled = true;
    bleConnectStatus.textContent = '';
    bleDeviceList.innerHTML = '<div class="device-list-empty">周囲のスマートローラー・パワーメーターを検索中...</div>';
    selectedBleDevice = null;
    postMessageToHost('scanBle');
  });

  // 選択デバイスへの接続
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

  btnSaveSettings.addEventListener('click', () => {
    const ftp = parseInt(settingFtp.value) || 200;
    const pedalBrake = settingPedalBrake.checked;
    currentFtp = ftp;
    postMessageToHost('updateSettings', { ftp, pedalBrake });

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
        } else {
          bleConnectStatus.textContent = `❌ 接続失敗: ${data.payload.name}`;
          btnConnectBle.disabled = false;
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
        if (typeof data.payload.pedalBrake !== 'undefined') {
          settingPedalBrake.checked = data.payload.pedalBrake;
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

  // BLEデバイス一覧の描画
  function renderBleDevices(devices) {
    bleScanningIndicator.classList.add('hidden');
    btnScanBle.disabled = false;

    if (!devices || devices.length === 0) {
      bleDeviceList.innerHTML = '<div class="device-list-empty">デバイスが見つかりませんでした。スマートローラーの電源やBluetoothがONになっているか確認してください。</div>';
      return;
    }

    bleDeviceList.innerHTML = '';
    devices.forEach(dev => {
      const item = document.createElement('div');
      item.className = 'device-item';
      
      const isPower = dev.SensorTypeInt === 2;
      item.innerHTML = `
        <div class="device-item-left">
          <span class="device-item-name">${dev.Name}</span>
          <span class="device-item-sub">MAC: ${dev.FormattedMac} ${dev.IsPaired ? '[ペアリング済]' : ''}</span>
        </div>
        <span class="device-item-badge ${isPower ? 'power' : ''}">${dev.TypeName}</span>
      `;

      item.addEventListener('click', () => {
        document.querySelectorAll('.device-item').forEach(el => el.classList.remove('selected'));
        item.classList.add('selected');
        selectedBleDevice = dev;
        btnConnectBle.disabled = false;
        bleConnectStatus.textContent = `選択中: ${dev.Name}`;
      });

      bleDeviceList.appendChild(item);
    });
  }

  // 状態スナップショットのUI反映
  function handleStateUpdate(s) {
    // 1. パワー
    if (s.Ftp && s.Ftp > 0) {
      currentFtp = s.Ftp;
    }
    const power = Math.round(s.Power);
    powerVal.textContent = power;
    const wkg = (power / 70.0).toFixed(1); // 基準体重70kg換算
    wkgVal.textContent = `${wkg} W/kg`;
    const zonePercent = Math.min(100, (power / (currentFtp * 1.5)) * 100);
    powerZoneIndicator.style.width = `${zonePercent}%`;

    // 2. 速度
    carSpeedVal.textContent = s.CarSpeedKmh.toFixed(1);
    targetSpeedVal.textContent = s.TargetSpeedKmh.toFixed(1);

    // 3. 勾配・標高
    const gradeSign = s.RawGrade >= 0 ? '+' : '';
    gradeVal.textContent = `${gradeSign}${s.RawGrade.toFixed(1)}`;
    sentGradeSub.textContent = `送信: ${s.SentGrade.toFixed(1)}%`;

    elevationVal.textContent = Math.round(s.ElevationMeters);
    gainSub.textContent = `獲得標高: +${Math.round(s.ElevationGainMeters)} m`;

    // 4. ケイデンス・ギア・距離
    cadenceVal.textContent = s.Cadence > 0 ? Math.round(s.Cadence) : '--';
    gearVal.textContent = s.IsTelemetryActive ? (s.Gear === 0 ? 'R' : `${s.Gear}`) : '--';
    distVal.textContent = s.TotalDistanceKm.toFixed(2);

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

    // 8. 走行プロファイル更新 (標高・速度・目標速度・パワー)
    if (profCurEle) profCurEle.textContent = `${Math.round(s.ElevationMeters)} m`;
    if (profSpeed) profSpeed.innerHTML = `${s.CarSpeedKmh.toFixed(1)} / ${s.TargetSpeedKmh.toFixed(1)} <span style="font-size:12px; font-weight:normal; color:var(--text-dim);">km/h</span>`;
    if (profPower) profPower.innerHTML = `${Math.round(s.Power)} <span style="font-size:12px; font-weight:normal; color:var(--text-dim);">W</span>`;
    if (profEleGain) profEleGain.textContent = `+${Math.round(s.ElevationGainMeters)} m`;
    if (profDist) profDist.textContent = `${s.TotalDistanceKm.toFixed(2)} km`;
    elevationChart.update(s.ElevationMeters, s.TotalDistanceKm, s.CarSpeedKmh, s.TargetSpeedKmh, s.Power);

    // 9. ステータスインジケーター
    bleDot.className = `status-dot ${s.IsBleConnected ? 'connected' : 'disconnected'}`;
    bleText.textContent = `BLE: ${s.BleStatus}`;
    if (s.ConnectedDeviceName && s.ConnectedDeviceName !== 'None') {
      currentDeviceText.textContent = s.ConnectedDeviceName;
    }

    udpDot.className = `status-dot ${s.IsTelemetryActive ? 'connected' : 'disconnected'}`;
    udpText.textContent = `Forza UDP: ${s.IsTelemetryActive ? '60Hz 受信中' : '待機中'}`;

    vjoyDot.className = `status-dot ${s.IsVJoyActive ? 'connected' : 'disconnected'}`;
    vjoyText.textContent = `vJoy: ${s.IsVJoyActive ? 'Active' : '未検出'}`;
  }

  function handleSessionSaved(summary) {
    lastSavedFilePath = summary.SavedFilePath;
    saveToast.classList.remove('hidden');
    appendLog(`[SESSION] 走行ログ保存完了: ${summary.SavedFilePath}`);
    if (historyController) {
      historyController.refreshList();
    }
  }

  // 初期化完了をC#ホストに通知
  postMessageToHost('ready');
});
