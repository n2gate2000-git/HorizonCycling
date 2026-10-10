// HorizonCycling Studio - In-Game Workout Overlay Controller

(function () {
  'use strict';

  // DOM要素
  const container = document.getElementById('overlayContainer');
  const sliderOpacity = document.getElementById('sliderOpacity');
  const btnToggleClickThrough = document.getElementById('btnToggleClickThrough');
  const clickThroughNotice = document.getElementById('clickThroughNotice');

  // コントロールボタン
  const btnPrev = document.getElementById('btnOverlayPrev');
  const btnPause = document.getElementById('btnOverlayPause');
  const btnSkip = document.getElementById('btnOverlaySkip');
  const btnStop = document.getElementById('btnOverlayStop');

  // セグメントリスト
  const workoutNameBadge = document.getElementById('workoutNameBadge');
  const workoutProgressTime = document.getElementById('workoutProgressTime');
  const segmentListItems = document.getElementById('segmentListItems');

  // アーチHUD
  const archBlockTitle = document.getElementById('archBlockTitle');
  const archBlockRemaining = document.getElementById('archBlockRemaining');
  const archTargetPower = document.getElementById('archTargetPower');
  const archActualPower = document.getElementById('archActualPower');
  const archPowerDiff = document.getElementById('archPowerDiff');
  const gaugePointer = document.getElementById('gaugePointer');
  const archTargetCadence = document.getElementById('archTargetCadence');
  const archActualCadence = document.getElementById('archActualCadence');
  const archProgressBar = document.getElementById('archProgressBar');

  // カウントダウン & メッセージ
  const countdownBanner = document.getElementById('countdownBanner');
  const countdownNumber = document.getElementById('countdownNumber');
  const coachMessageBanner = document.getElementById('coachMessageBanner');
  const coachMessageText = document.getElementById('coachMessageText');

  // 下部リボン
  const ribbonBlocks = document.getElementById('ribbonBlocks');
  const ribbonPin = document.getElementById('ribbonPin');
  const ribbonPinTime = document.getElementById('ribbonPinTime');

  // 内部状態
  let isClickThrough = true;
  let lastWorkoutTitle = '';
  let lastSegmentsJson = '';

  // ゾーン色マップ
  const ZONE_COLORS = {
    1: '#64748b',
    2: '#3b82f6',
    3: '#10b981',
    4: '#eab308',
    5: '#f97316',
    6: '#ef4444',
    7: '#a855f7'
  };

  // 初期化
  function init() {
    setupEventListeners();
    setupIpcListener();
  }

  function setupEventListeners() {
    // 透過度スライダー
    sliderOpacity.addEventListener('input', (e) => {
      const val = e.target.value / 100;
      container.style.opacity = val;
    });

    // クリック透過切り替え
    btnToggleClickThrough.addEventListener('click', () => {
      setClickThrough(!isClickThrough);
    });

    // ワークアウト操作
    btnPrev.addEventListener('click', (e) => {
      e.stopPropagation();
      postAction('prevStep');
    });
    btnPause.addEventListener('click', (e) => {
      e.stopPropagation();
      const isPaused = btnPause.textContent === '▶';
      postAction(isPaused ? 'resumeWorkout' : 'pauseWorkout');
    });
    btnSkip.addEventListener('click', (e) => {
      e.stopPropagation();
      postAction('skipStep');
    });
    const triggerStop = (e) => {
      e.preventDefault();
      e.stopPropagation();
      postAction('stopWorkout');
    };
    btnStop.addEventListener('click', triggerStop);
    btnStop.addEventListener('mousedown', triggerStop);

    // ショートカットキー
    window.addEventListener('keydown', (e) => {
      if (e.ctrlKey && e.shiftKey) {
        if (e.key === 'O' || e.key === 'o') {
          setClickThrough(!isClickThrough);
        } else if (e.key === 'Q' || e.key === 'q') {
          postAction('stopWorkout');
        }
      }
    });
  }

  function setupIpcListener() {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.addEventListener('message', (event) => {
        const msg = event.data;
        if (!msg) return;

        if (msg.type === 'overlayState') {
          updateOverlayState(msg);
        } else if (msg.type === 'clickThroughChanged') {
          updateClickThroughUi(msg.enabled);
        }
      });
    }
  }

  function postAction(action, extra = {}) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage({ action, ...extra });
    }
  }

  function setClickThrough(enable) {
    isClickThrough = enable;
    updateClickThroughUi(enable);
    postAction('setClickThrough', { enabled: enable });
  }

  function updateClickThroughUi(enable) {
    isClickThrough = enable;
    if (enable) {
      document.body.classList.remove('edit-mode');
      btnToggleClickThrough.textContent = '🛡️ 操作透過中';
      btnToggleClickThrough.classList.remove('tool-btn-danger');
      btnToggleClickThrough.classList.add('tool-btn-accent');
      clickThroughNotice.classList.add('hide');
    } else {
      document.body.classList.add('edit-mode');
      btnToggleClickThrough.textContent = '🔓 編集モード (クリック可)';
      btnToggleClickThrough.classList.remove('tool-btn-accent');
      btnToggleClickThrough.classList.add('tool-btn-danger');
      clickThroughNotice.classList.remove('hide');
    }
  }

  function updateOverlayState(data) {
    const workout = data.workout;
    if (!workout || !workout.IsActive) {
      container.style.display = 'none';
      return;
    }
    container.style.display = 'block';

    const actualPower = Math.round(data.power || 0);
    const actualCadence = Math.round(data.cadence || 0);
    const targetWatts = workout.TargetWatts || 0;
    const targetCad = workout.TargetCadence;

    // ポーズボタンの表示切替
    btnPause.textContent = workout.State === 'Paused' ? '▶' : '⏸';

    // 1. タイトル & 時間
    workoutNameBadge.textContent = workout.WorkoutTitle || 'Workout';
    workoutProgressTime.textContent = `${formatTime(workout.ElapsedSec)} / ${formatTime(workout.TotalDurationSec)}`;

    // 2. アーチ情報
    archBlockTitle.textContent = `BLOCK ${workout.CurrentSegmentIndex + 1}/${workout.TotalSegmentsCount}: ${workout.CurrentSegmentType.toUpperCase()}`;
    archBlockRemaining.textContent = `⏱ ${formatTime(workout.SegmentRemainingSec)}`;

    archTargetPower.textContent = targetWatts;
    archActualPower.textContent = actualPower;

    // パワー差分
    const diff = actualPower - targetWatts;
    archPowerDiff.textContent = `(${diff >= 0 ? '+' : ''}${diff}W)`;
    if (Math.abs(diff) <= 8) {
      archPowerDiff.className = 'val-diff match';
    } else if (diff < -8) {
      archPowerDiff.className = 'val-diff low';
    } else {
      archPowerDiff.className = 'val-diff high';
    }

    // ゲージ針の位置 (-50W〜+50Wを 0%〜100%にマッピング)
    const clampedDiff = Math.max(-40, Math.min(40, diff));
    const gaugePct = 50 + (clampedDiff / 40) * 45; // 5%〜95%
    gaugePointer.style.left = `${gaugePct}%`;
    if (Math.abs(diff) <= 8) {
      gaugePointer.style.background = 'var(--accent-green)';
    } else if (diff < -8) {
      gaugePointer.style.background = 'var(--accent-yellow)';
    } else {
      gaugePointer.style.background = 'var(--accent-red)';
    }

    // ケイデンス
    archTargetCadence.textContent = targetCad ? targetCad : '--';
    archActualCadence.textContent = actualCadence > 0 ? actualCadence : '--';

    // セグメント内進行率
    const segTotal = workout.SegmentTotalDurationSec > 0 ? workout.SegmentTotalDurationSec : 1;
    const segPct = Math.min(100, (workout.SegmentElapsedSec / segTotal) * 100);
    archProgressBar.style.width = `${segPct}%`;

    // 3. 次ブロックカウントダウン (残り5秒以下)
    if (workout.SegmentRemainingSec > 0 && workout.SegmentRemainingSec <= 5 && workout.CurrentSegmentIndex + 1 < workout.TotalSegmentsCount) {
      countdownNumber.textContent = workout.SegmentRemainingSec;
      countdownBanner.classList.remove('hide');
    } else {
      countdownBanner.classList.add('hide');
    }

    // 4. 字幕コーチングメッセージ
    if (workout.ActiveMessage) {
      coachMessageText.textContent = workout.ActiveMessage;
      coachMessageBanner.classList.remove('hide');
    } else {
      coachMessageBanner.classList.add('hide');
    }

    // 5. セグメントリスト描画（変更時のみ更新）
    renderSegmentList(workout);

    // 6. 下部プロファイルリボン描画
    renderRibbon(workout);
  }

  function renderSegmentList(workout) {
    if (!workout.Segments || workout.Segments.length === 0) return;

    // 現在のセグメントの周辺（前後3つ）を中心に表示
    const currentIdx = workout.CurrentSegmentIndex;
    const items = workout.Segments;

    // 差分チェック
    const segKey = `${currentIdx}_${workout.SegmentRemainingSec}`;
    if (segKey === lastSegmentsJson) return;
    lastSegmentsJson = segKey;

    let html = '';
    const startIdx = Math.max(0, currentIdx - 1);
    const endIdx = Math.min(items.length, currentIdx + 4);

    for (let i = startIdx; i < endIdx; i++) {
      const seg = items[i];
      const isCur = i === currentIdx;
      const isComp = i < currentIdx;

      const zoneColor = ZONE_COLORS[seg.Zone] || '#64748b';
      const statusClass = isComp ? 'completed' : isCur ? 'current' : '';
      const timeStr = isCur ? formatTime(workout.SegmentRemainingSec) : formatTime(seg.DurationSec);

      html += `
        <div class="seg-item ${statusClass}">
          <div class="seg-info">
            <span class="seg-zone-tag" style="background:${zoneColor}">Z${seg.Zone}</span>
            <span class="seg-label">${seg.Type}</span>
          </div>
          <div class="seg-meta">
            <span>${seg.TargetWattsLow}W</span> • <span>${timeStr}</span>
          </div>
        </div>
      `;
    }

    segmentListItems.innerHTML = html;
  }

  function renderRibbon(workout) {
    if (!workout.Segments || workout.Segments.length === 0) return;

    // 初回描画またはタイトル変更時のみブロック再構築
    if (lastWorkoutTitle !== workout.WorkoutTitle) {
      lastWorkoutTitle = workout.WorkoutTitle;
      const totalSec = workout.TotalDurationSec > 0 ? workout.TotalDurationSec : 1;

      let blocksHtml = '';
      workout.Segments.forEach((seg) => {
        const widthPct = (seg.DurationSec / totalSec) * 100;
        const color = ZONE_COLORS[seg.Zone] || '#64748b';
        blocksHtml += `<div class="ribbon-block" id="ribbonSeg_${seg.Index}" style="width:${widthPct}%; background:${color};"></div>`;
      });
      ribbonBlocks.innerHTML = blocksHtml;
    }

    // 各ブロックのハイライト更新
    workout.Segments.forEach((seg) => {
      const el = document.getElementById(`ribbonSeg_${seg.Index}`);
      if (el) {
        if (seg.Index < workout.CurrentSegmentIndex) {
          el.className = 'ribbon-block completed';
        } else if (seg.Index === workout.CurrentSegmentIndex) {
          el.className = 'ribbon-block current';
        } else {
          el.className = 'ribbon-block';
        }
      }
    });

    // ピンの位置更新
    const totalSec = workout.TotalDurationSec > 0 ? workout.TotalDurationSec : 1;
    const progressPct = Math.min(100, Math.max(0, (workout.ElapsedSec / totalSec) * 100));
    ribbonPin.style.left = `${progressPct}%`;
    ribbonPinTime.textContent = formatTime(workout.ElapsedSec);
  }

  function formatTime(totalSec) {
    if (isNaN(totalSec) || totalSec < 0) totalSec = 0;
    const m = Math.floor(totalSec / 60);
    const s = Math.floor(totalSec % 60);
    return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
  }

  // 初期化実行
  document.addEventListener('DOMContentLoaded', init);
})();
