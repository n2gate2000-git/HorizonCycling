// HorizonCycling Studio - Workout Library & Controller

(function () {
  'use strict';

  // DOM要素
  const btnToggleOverlay = document.getElementById('btnToggleOverlay');
  const workoutSearchInput = document.getElementById('workoutSearchInput');
  const btnImportWorkout = document.getElementById('btnImportWorkout');
  const btnOpenWorkoutsFolder = document.getElementById('btnOpenWorkoutsFolder');
  const btnRefreshWorkouts = document.getElementById('btnRefreshWorkouts');
  const workoutCategoriesList = document.getElementById('workoutCategoriesList');

  // プレビュー領域
  const workoutPreviewEmpty = document.getElementById('workoutPreviewEmpty');
  const workoutPreviewContent = document.getElementById('workoutPreviewContent');
  const prevTitle = document.getElementById('prevTitle');
  const prevAuthor = document.getElementById('prevAuthor');
  const prevTags = document.getElementById('prevTags');
  const prevDesc = document.getElementById('prevDesc');
  const prevDuration = document.getElementById('prevDuration');
  const prevTss = document.getElementById('prevTss');
  const prevIf = document.getElementById('prevIf');
  const prevZone = document.getElementById('prevZone');
  const prevKcal = document.getElementById('prevKcal');
  const prevFtpVal = document.getElementById('prevFtpVal');
  const workoutPreviewCanvas = document.getElementById('workoutPreviewCanvas');
  const prevStepsCount = document.getElementById('prevStepsCount');
  const prevStepsList = document.getElementById('prevStepsList');
  const btnStartWorkout = document.getElementById('btnStartWorkout');

  // 実行中コントロール
  const workoutActiveControls = document.getElementById('workoutActiveControls');
  const btnPrevActive = document.getElementById('btnPrevActive');
  const btnPauseActive = document.getElementById('btnPauseActive');
  const btnSkipActive = document.getElementById('btnSkipActive');
  const btnStopActive = document.getElementById('btnStopActive');

  // ランプテスト関連
  const btnStartRampTest = document.getElementById('btnStartRampTest');
  const modalRampResult = document.getElementById('modalRampResult');
  const btnCloseRampModal = document.getElementById('btnCloseRampModal');
  const btnCancelRampFtp = document.getElementById('btnCancelRampFtp');
  const btnApplyRampFtp = document.getElementById('btnApplyRampFtp');
  const rampModalNewFtp = document.getElementById('rampModalNewFtp');
  const rampModalFtpDelta = document.getElementById('rampModalFtpDelta');
  const rampModalOldFtp = document.getElementById('rampModalOldFtp');
  const rampModalBest1Min = document.getElementById('rampModalBest1Min');
  const rampModalDuration = document.getElementById('rampModalDuration');

  // 内部状態
  let libraryItems = [];
  let selectedFilePath = null;
  let currentDetail = null;
  let currentFtp = 200.0;
  let activeWorkoutSnapshot = null;
  let pendingRampFtp = null;

  // ゾーン色
  const ZONE_COLORS = {
    1: '#64748b',
    2: '#3b82f6',
    3: '#10b981',
    4: '#eab308',
    5: '#f97316',
    6: '#ef4444',
    7: '#a855f7'
  };

  function getZoneName(zone) {
    const isEn = window.i18n && window.i18n.getLanguage() === 'en';
    const namesJa = {
      1: '回復 (Z1)',
      2: '有酸素 (Z2)',
      3: 'テンポ (Z3)',
      4: '閾値 (Z4)',
      5: 'VO2Max (Z5)',
      6: '無酸素 (Z6)',
      7: '神経筋 (Z7)'
    };
    const namesEn = {
      1: 'Recovery (Z1)',
      2: 'Endurance (Z2)',
      3: 'Tempo (Z3)',
      4: 'Threshold (Z4)',
      5: 'VO2Max (Z5)',
      6: 'Anaerobic (Z6)',
      7: 'Neuromuscular (Z7)'
    };
    return (isEn ? namesEn[zone] : namesJa[zone]) || `Z${zone}`;
  }

  function init() {
    setupEventListeners();
    setupSoundSettings();
    setupIpcListener();

    window.addEventListener('languageChanged', () => {
      if (currentWorkoutDetail) {
        renderWorkoutDetails(currentWorkoutDetail);
      }
      renderLibraryList();
    });

    // 初期ライブラリ取得
    setTimeout(() => {
      postAction('getWorkoutLibrary');
    }, 500);
  }

  function setupEventListeners() {
    // ヘッダー 最前面HUDトグル
    if (btnToggleOverlay) {
      btnToggleOverlay.addEventListener('click', () => {
        postAction('toggleOverlay');
      });
    }

    // 検索入力
    workoutSearchInput.addEventListener('input', () => {
      renderLibrary();
    });

    // アクションボタン
    btnImportWorkout.addEventListener('click', () => {
      postAction('importWorkoutFilesDialog');
    });

    btnOpenWorkoutsFolder.addEventListener('click', () => {
      postAction('openWorkoutsFolder');
    });

    btnRefreshWorkouts.addEventListener('click', () => {
      workoutCategoriesList.innerHTML = '<div class="workout-loading">更新中...</div>';
      postAction('getWorkoutLibrary');
    });

    // ランプテスト開始
    if (btnStartRampTest) {
      btnStartRampTest.addEventListener('click', () => {
        window.workoutSoundEngine?.init();
        if (confirm('⚡ FTP自動計測ランプテストを開始しますか？\n\n【テスト仕様】\n・ウォームアップ(100W)から開始し、1分ごとに20Wずつ自動上昇\n・ケイデンスが50rpmを下回ると限界判定で自動終了\n・直前1分間の最大平均パワーの75%から新FTPを算出します')) {
          postAction('startRampTest');
        }
      });
    }

    // ランプテストモーダル操作
    if (btnCloseRampModal) {
      btnCloseRampModal.addEventListener('click', () => {
        modalRampResult?.classList.add('hidden');
      });
    }
    if (btnCancelRampFtp) {
      btnCancelRampFtp.addEventListener('click', () => {
        modalRampResult?.classList.add('hidden');
      });
    }
    if (btnApplyRampFtp) {
      btnApplyRampFtp.addEventListener('click', () => {
        if (pendingRampFtp && pendingRampFtp > 0) {
          postAction('applyRampTestFtp', { newFtp: pendingRampFtp });
          modalRampResult?.classList.add('hidden');
          alert(`🎉 新FTP ${Math.round(pendingRampFtp)}W を保存・適用しました！`);
        }
      });
    }

    // ワークアウト開始
    btnStartWorkout.addEventListener('click', () => {
      window.workoutSoundEngine?.init();
      if (selectedFilePath) {
        postAction('startWorkout', { filePath: selectedFilePath });
      }
    });

    // 実行中操作
    btnPrevActive.addEventListener('click', () => postAction('previousWorkoutStep'));
    btnPauseActive.addEventListener('click', () => {
      if (activeWorkoutSnapshot && activeWorkoutSnapshot.State === 'Paused') {
        postAction('resumeWorkout');
      } else {
        postAction('pauseWorkout');
      }
    });
    btnSkipActive.addEventListener('click', () => postAction('skipWorkoutStep'));
    btnStopActive.addEventListener('click', () => postAction('stopWorkout'));

    // ドラッグ＆ドロップ取込
    const dragTarget = document.getElementById('tabWorkouts');
    if (dragTarget) {
      dragTarget.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.stopPropagation();
      });
      dragTarget.addEventListener('drop', (e) => {
        e.preventDefault();
        e.stopPropagation();
        // WebMessage側でダイアログを促す
        postAction('importWorkoutFilesDialog');
      });
    }
  }

  function setupIpcListener() {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.addEventListener('message', (event) => {
        const msg = event.data;
        if (!msg) return;

        switch (msg.type) {
          case 'workoutLibrary':
            libraryItems = msg.payload || [];
            renderLibrary();
            break;

          case 'workoutDetail':
            if (msg.payload) {
              currentDetail = msg.payload;
              renderDetail(msg.payload);
            }
            break;

          case 'config':
            if (msg.payload && msg.payload.ftp) {
              currentFtp = msg.payload.ftp;
              prevFtpVal.textContent = Math.round(currentFtp);
              if (currentDetail) {
                renderCanvasProfile(currentDetail);
              }
            }
            break;

          case 'state':
            handleStateUpdate(msg.payload);
            break;

          case 'rampTestResult':
            if (msg.payload) {
              handleRampTestResult(msg.payload);
            }
            break;
        }
      });
    }
  }

  function postAction(action, extra = {}) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage({ action, ...extra });
    }
  }

  function handleRampTestResult(result) {
    if (!result || !modalRampResult) return;

    pendingRampFtp = result.NewFtp;

    if (rampModalNewFtp) {
      rampModalNewFtp.innerHTML = `${Math.round(result.NewFtp)} <span class="unit">W</span>`;
    }
    if (rampModalFtpDelta) {
      const delta = result.FtpDelta;
      const sign = delta >= 0 ? '+' : '';
      const pct = result.OldFtp > 0 ? ((delta / result.OldFtp) * 100).toFixed(1) : 0;
      rampModalFtpDelta.textContent = `${sign}${delta} W (${sign}${pct}%)`;
      rampModalFtpDelta.className = delta >= 0 ? 'ramp-ftp-diff text-green' : 'ramp-ftp-diff text-orange';
    }
    if (rampModalOldFtp) {
      rampModalOldFtp.textContent = `${Math.round(result.OldFtp)} W`;
    }
    if (rampModalBest1Min) {
      rampModalBest1Min.textContent = `${Math.round(result.Best1MinPower)} W`;
    }
    if (rampModalDuration) {
      rampModalDuration.textContent = result.FormattedTestDuration || `${Math.floor(result.TotalTestDurationSec / 60)}分${result.TotalTestDurationSec % 60}秒`;
    }

    modalRampResult.classList.remove('hidden');
  }

  function handleStateUpdate(state) {
    if (!state) return;

    if (state.Ftp && state.Ftp !== currentFtp) {
      currentFtp = state.Ftp;
      prevFtpVal.textContent = Math.round(currentFtp);
    }

    const workout = state.Workout;
    activeWorkoutSnapshot = workout;

    // 音響エンジンへワークアウト状態をリアルタイム連携
    if (window.workoutSoundEngine) {
      window.workoutSoundEngine.update(workout);
    }

    const isEn = window.i18n && window.i18n.getLanguage() === 'en';
    if (state.IsWorkoutActive && workout) {
      workoutActiveControls.classList.remove('hidden');
      btnPauseActive.textContent = workout.State === 'Paused' 
        ? (isEn ? '▶ Resume' : '▶ 再開') 
        : (isEn ? '⏸ Pause' : '⏸ 一時停止');
      btnStartWorkout.textContent = isEn ? '● Workout In Progress' : '● ワークアウト実行中';
      btnStartWorkout.disabled = true;
      btnStartWorkout.style.opacity = '0.6';
    } else {
      workoutActiveControls.classList.add('hidden');
      btnStartWorkout.textContent = isEn ? '▶ Start Workout' : '▶ このワークアウトを開始';
      btnStartWorkout.disabled = false;
      btnStartWorkout.style.opacity = '1.0';
    }
  }

  // ワークアウト音響設定の初期化 & リスナー登録
  function setupSoundSettings() {
    const se = window.workoutSoundEngine;
    if (!se) return;

    const settingSoundEnabled = document.getElementById('settingSoundEnabled');
    const settingSoundVolume = document.getElementById('settingSoundVolume');
    const settingSoundVolumeVal = document.getElementById('settingSoundVolumeVal');
    const settingTransitionSound = document.getElementById('settingTransitionSound');
    const settingCountdownSound = document.getElementById('settingCountdownSound');
    const settingTtsEnabled = document.getElementById('settingTtsEnabled');

    const btnTestWorkSound = document.getElementById('btnTestWorkSound');
    const btnTestRestSound = document.getElementById('btnTestRestSound');
    const btnTestCountdownSound = document.getElementById('btnTestCountdownSound');
    const btnTestTts = document.getElementById('btnTestTts');

    // 初期値の同期
    if (settingSoundEnabled) settingSoundEnabled.checked = se.enabled;
    if (settingSoundVolume) {
      const volPct = Math.round(se.volume * 100);
      settingSoundVolume.value = volPct;
      if (settingSoundVolumeVal) settingSoundVolumeVal.textContent = `${volPct}%`;
    }
    if (settingTransitionSound) settingTransitionSound.checked = se.transitionEnabled;
    if (settingCountdownSound) settingCountdownSound.checked = se.countdownEnabled;
    if (settingTtsEnabled) settingTtsEnabled.checked = se.ttsEnabled;

    // トグル・スライダー変更監視
    if (settingSoundEnabled) {
      settingSoundEnabled.addEventListener('change', (e) => {
        se.enabled = e.target.checked;
        se.saveSettings();
      });
    }

    if (settingSoundVolume) {
      settingSoundVolume.addEventListener('input', (e) => {
        const val = parseInt(e.target.value, 10);
        se.volume = val / 100;
        if (settingSoundVolumeVal) settingSoundVolumeVal.textContent = `${val}%`;
        se.saveSettings();
      });
    }

    if (settingTransitionSound) {
      settingTransitionSound.addEventListener('change', (e) => {
        se.transitionEnabled = e.target.checked;
        se.saveSettings();
      });
    }

    if (settingCountdownSound) {
      settingCountdownSound.addEventListener('change', (e) => {
        se.countdownEnabled = e.target.checked;
        se.saveSettings();
      });
    }

    if (settingTtsEnabled) {
      settingTtsEnabled.addEventListener('change', (e) => {
        se.ttsEnabled = e.target.checked;
        se.saveSettings();
        if (se.ttsEnabled) {
          const isEn = window.i18n && window.i18n.getLanguage() === 'en';
          se.speak(isEn ? 'Voice guidance enabled' : '音声ガイダンスを有効にしました', true);
        }
      });
    }

    // テスト試聴ボタン
    if (btnTestWorkSound) {
      btnTestWorkSound.addEventListener('click', (e) => {
        e.preventDefault();
        se.init();
        se.playWorkStart();
      });
    }

    if (btnTestRestSound) {
      btnTestRestSound.addEventListener('click', (e) => {
        e.preventDefault();
        se.init();
        se.playRestStart();
      });
    }

    if (btnTestCountdownSound) {
      btnTestCountdownSound.addEventListener('click', (e) => {
        e.preventDefault();
        se.init();
        se.playCountdownTick(1);
      });
    }

    if (btnTestTts) {
      btnTestTts.addEventListener('click', (e) => {
        e.preventDefault();
        se.init();
        const isEn = window.i18n && window.i18n.getLanguage() === 'en';
        se.speak(isEn ? 'Interval start, target 250 watts, 5 minutes' : 'インターバル開始、目標250ワット、5分間', true);
      });
    }
  }

  // ライブラリ一覧描画
  function renderLibrary() {
    const query = workoutSearchInput.value.trim().toLowerCase();

    // フィルタリング
    const filtered = libraryItems.filter((item) => {
      if (!query) return true;
      const matchTitle = item.Title.toLowerCase().includes(query);
      const matchDesc = (item.Description || '').toLowerCase().includes(query);
      const matchAuthor = (item.Author || '').toLowerCase().includes(query);
      const matchCat = (item.Category || '').toLowerCase().includes(query);
      const matchTag = (item.Tags || []).some(t => t.toLowerCase().includes(query));
      return matchTitle || matchDesc || matchAuthor || matchCat || matchTag;
    });

    if (filtered.length === 0) {
      workoutCategoriesList.innerHTML = '<div class="workout-loading">該当するワークアウトがありません</div>';
      return;
    }

    // カテゴリごとにグループ化
    const groups = {};
    filtered.forEach((item) => {
      const cat = item.Category || 'General';
      if (!groups[cat]) groups[cat] = [];
      groups[cat].push(item);
    });

    let html = '';
    for (const [catName, items] of Object.entries(groups)) {
      html += `
        <div class="category-group">
          <div class="category-header">
            <span>▼ ${escapeHtml(catName)}</span>
            <span class="category-count">(${items.length})</span>
          </div>
          <div class="category-items">
      `;

      items.forEach((item) => {
        const isActive = item.FilePath === selectedFilePath ? 'active' : '';
        const zoneNum = parseInt((item.PrimaryZone || 'Z3').replace('Z', ''), 10) || 3;
        const zoneColor = ZONE_COLORS[zoneNum] || '#10b981';

        html += `
          <div class="workout-item-card ${isActive}" data-path="${escapeHtml(item.FilePath)}">
            <div class="workout-item-title">${escapeHtml(item.Title)}</div>
            <div class="workout-item-meta">
              <span>⏱ ${item.FormattedDuration}</span>
              <div class="workout-item-badges">
                <span class="zone-badge-tag" style="background:${zoneColor}">${item.PrimaryZone}</span>
                <span>TSS ${item.EstimatedTss}</span>
              </div>
            </div>
          </div>
        `;
      });

      html += `
          </div>
        </div>
      `;
    }

    workoutCategoriesList.innerHTML = html;

    // クリックイベント
    const cards = workoutCategoriesList.querySelectorAll('.workout-item-card');
    cards.forEach((card) => {
      card.addEventListener('click', () => {
        const path = card.getAttribute('data-path');
        selectWorkout(path);
      });
    });

    // 以前選択されていたものがあれば再ハイライト、なければ最初を選択
    if (!selectedFilePath && filtered.length > 0) {
      selectWorkout(filtered[0].FilePath);
    }
  }

  function selectWorkout(filePath) {
    selectedFilePath = filePath;

    // アクティブクラス更新
    const cards = workoutCategoriesList.querySelectorAll('.workout-item-card');
    cards.forEach((card) => {
      if (card.getAttribute('data-path') === filePath) {
        card.classList.add('active');
      } else {
        card.classList.remove('active');
      }
    });

    // 詳細取得
    postAction('loadWorkoutDetail', { filePath });
  }

  // 選択されたワークアウト詳細の描画
  function renderDetail(detail) {
    if (!detail) return;

    workoutPreviewEmpty.classList.add('hidden');
    workoutPreviewContent.classList.remove('hidden');

    const isEn = window.i18n && window.i18n.getLanguage() === 'en';
    prevTitle.textContent = detail.Title || 'Untitled Workout';
    prevAuthor.textContent = detail.Author ? (isEn ? `By: ${detail.Author}` : `作: ${detail.Author}`) : '';
    prevDesc.textContent = detail.Description || (isEn ? 'No description available.' : '説明はありません。');

    // タグ
    if (detail.Tags && detail.Tags.length > 0) {
      prevTags.innerHTML = detail.Tags.map(t => `<span class="prev-tag-pill">${escapeHtml(t)}</span>`).join('');
    } else {
      prevTags.innerHTML = '';
    }

    // スタッツ
    const m = Math.floor(detail.TotalDurationSec / 60);
    const s = detail.TotalDurationSec % 60;
    prevDuration.textContent = isEn ? `${m}m ${s.toString().padStart(2, '0')}s` : `${m}分${s.toString().padStart(2, '0')}秒`;
    prevTss.textContent = detail.EstimatedTss;
    prevIf.textContent = detail.EstimatedIf;

    const zNum = parseInt((detail.PrimaryZone || 'Z3').replace('Z', ''), 10) || 3;
    prevZone.textContent = getZoneName(zNum);
    prevZone.style.color = ZONE_COLORS[zNum] || '#10b981';

    prevKcal.textContent = `${detail.EstimatedKcal || Math.round(detail.EstimatedTss * 7.5)} kcal`;
    prevFtpVal.textContent = Math.round(currentFtp);

    // プロファイルキャンバス描画
    renderCanvasProfile(detail);

    // ステップ一覧
    renderStepsList(detail);
  }

  // Canvasプロファイル描画
  function renderCanvasProfile(detail) {
    const canvas = workoutPreviewCanvas;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    const width = canvas.width;
    const height = canvas.height;

    ctx.clearRect(0, 0, width, height);

    const totalSec = detail.TotalDurationSec > 0 ? detail.TotalDurationSec : 1;
    const maxWatts = Math.max(300, currentFtp * 1.5);

    // 背景グリッド & FTPライン
    const ftpY = height - (currentFtp / maxWatts) * (height - 20) - 10;
    ctx.strokeStyle = 'rgba(255, 255, 255, 0.08)';
    ctx.lineWidth = 1;

    // 水平グリッド (50W, 100W, 150W, 200W...)
    for (let w = 100; w <= maxWatts; w += 100) {
      const y = height - (w / maxWatts) * (height - 20) - 10;
      ctx.beginPath();
      ctx.moveTo(0, y);
      ctx.lineTo(width, y);
      ctx.stroke();
    }

    // FTP破線
    ctx.strokeStyle = 'rgba(239, 68, 68, 0.6)';
    ctx.setLineDash([4, 4]);
    ctx.beginPath();
    ctx.moveTo(0, ftpY);
    ctx.lineTo(width, ftpY);
    ctx.stroke();
    ctx.setLineDash([]);

    // FTPラベル
    ctx.fillStyle = 'rgba(239, 68, 68, 0.8)';
    ctx.font = '10px Roboto Mono, monospace';
    ctx.fillText(`FTP: ${Math.round(currentFtp)}W`, 8, ftpY - 4);

    // 各セグメントブロックの描画
    detail.Segments.forEach((seg) => {
      const x1 = (seg.StartSec / totalSec) * width;
      const segW = (seg.DurationSec / totalSec) * width;

      const wattsLow = seg.TargetPowerLowPct * currentFtp;
      const wattsHigh = seg.TargetPowerHighPct * currentFtp;

      const yLow = height - (wattsLow / maxWatts) * (height - 20) - 10;
      const yHigh = height - (wattsHigh / maxWatts) * (height - 20) - 10;

      // ゾーン色判定
      const zoneNum = getPowerZone(seg.TargetPowerLowPct);
      const color = ZONE_COLORS[zoneNum] || '#64748b';

      ctx.fillStyle = color;
      ctx.strokeStyle = 'rgba(0, 0, 0, 0.4)';
      ctx.lineWidth = 1;

      ctx.beginPath();
      ctx.moveTo(x1, height);
      ctx.lineTo(x1, yLow);
      ctx.lineTo(x1 + segW, yHigh);
      ctx.lineTo(x1 + segW, height);
      ctx.closePath();
      ctx.fill();
      ctx.stroke();

      // 目標ワット数テキスト（幅が十分ある場合）
      if (segW > 35) {
        ctx.fillStyle = '#fff';
        ctx.font = 'bold 9px Roboto Mono, monospace';
        const displayW = Math.round(wattsLow);
        ctx.fillText(`${displayW}W`, x1 + 4, Math.min(yLow, yHigh) - 4);
      }
    });
  }

  // ステップ詳細一覧描画
  function renderStepsList(detail) {
    prevStepsCount.textContent = detail.Segments.length;

    let html = '';
    detail.Segments.forEach((seg, idx) => {
      const zoneNum = getPowerZone(seg.TargetPowerLowPct);
      const zoneColor = ZONE_COLORS[zoneNum] || '#64748b';
      const wattsLow = Math.round(seg.TargetPowerLowPct * currentFtp);
      const wattsHigh = Math.round(seg.TargetPowerHighPct * currentFtp);

      const isEn = window.i18n && window.i18n.getLanguage() === 'en';
      const m = Math.floor(seg.DurationSec / 60);
      const s = seg.DurationSec % 60;
      const durStr = isEn ? `${m}m ${s > 0 ? `${s}s` : ''}` : `${m}分${s > 0 ? `${s}秒` : ''}`;

      const powerStr = Math.abs(wattsHigh - wattsLow) > 3 ? `${wattsLow}W ➔ ${wattsHigh}W` : `${wattsLow}W (${Math.round(seg.TargetPowerLowPct * 100)}%)`;
      const cadStr = seg.TargetCadence ? ` • ${isEn ? 'Cadence' : 'ケイデンス'} ${seg.TargetCadence}rpm` : '';

      html += `
        <div class="step-row">
          <div class="step-left">
            <span class="step-num">${idx + 1}</span>
            <span class="zone-badge-tag" style="background:${zoneColor}">Z${zoneNum}</span>
            <span class="step-type">${seg.SegmentType}</span>
          </div>
          <div class="step-right">
            <span>⏱ ${durStr}</span> • <span>${powerStr}</span>${cadStr}
          </div>
        </div>
      `;
    });

    prevStepsList.innerHTML = html;
  }

  function getPowerZone(pct) {
    if (pct < 0.55) return 1;
    if (pct < 0.75) return 2;
    if (pct < 0.90) return 3;
    if (pct < 1.05) return 4;
    if (pct < 1.20) return 5;
    if (pct < 1.50) return 6;
    return 7;
  }

  function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/[&<>'"]/g, tag => ({
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      "'": '&#39;',
      '"': '&quot;'
    }[tag] || tag));
  }

  document.addEventListener('DOMContentLoaded', init);
})();
