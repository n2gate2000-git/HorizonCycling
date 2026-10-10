/**
 * HorizonCycling Studio - Session History Controller
 * Manages saved activities, summary statistics, route map replay, and telemetry profile charts.
 */

class SessionHistoryController {
  constructor() {
    this.historyList = [];
    this.selectedSession = null;
    this.map = null;
    this.trackPolyline = null;
    this.hoverMarker = null;
    this.hoverOuter = null;
    this.hoverDot = null;
    this.chart = null;
    this.currentPoints = null;
    this.chartPoints = [];
    this.followPoint = true;
    this.btnFollowHistoryPoint = null;

    // マップ座標変換パラメータ (リアルタイム追跡・キャリブレーションと完全一致)
    this.mapOriginX = -86.5;
    this.mapOriginZ = 149.1;
    this.mapScale = 0.252;
    this.invertZ = true;

    // DOM 要素
    this.listContainer = null;
    this.mainPane = null;
    this.emptyState = null;
    this.detailPane = null;

    this.histTitleDate = null;
    this.histSessionIdBadge = null;
    this.histMovingTime = null;
    this.histTotalTime = null;
    this.histDistance = null;
    this.histAvgSpeed = null;
    this.histElevationGain = null;
    this.histMaxSpeed = null;
    this.histAvgPower = null;
    this.histMaxPower = null;
    this.histCalories = null;
    this.histAvgCadence = null;
    this.histAvgHr = null;
    this.histMaxHr = null;

    this.histHrZoneSection = null;
    this.histHrZoneSummary = null;
    this.histHrZoneDistBar = null;
    this.histHrZoneDistLegend = null;

    this.maxHeartRate = 190;
    this.restingHeartRate = 60;

    this.btnRefreshHistory = null;
    this.btnOpenActivitiesDir = null;
    this.btnOpenSelectedGpx = null;
    this.btnDeleteSelectedSession = null;
  }

  init() {
    this.listContainer = document.getElementById('historyListContainer');
    this.mainPane = document.getElementById('historyMainPane');
    this.emptyState = document.getElementById('historyEmptyState');
    this.detailPane = document.getElementById('historyDetailPane');

    this.histTitleDate = document.getElementById('histTitleDate');
    this.histSessionIdBadge = document.getElementById('histSessionIdBadge');
    this.histMovingTime = document.getElementById('histMovingTime');
    this.histTotalTime = document.getElementById('histTotalTime');
    this.histDistance = document.getElementById('histDistance');
    this.histAvgSpeed = document.getElementById('histAvgSpeed');
    this.histElevationGain = document.getElementById('histElevationGain');
    this.histMaxSpeed = document.getElementById('histMaxSpeed');
    this.histAvgPower = document.getElementById('histAvgPower');
    this.histMaxPower = document.getElementById('histMaxPower');
    this.histCalories = document.getElementById('histCalories');
    this.histAvgCadence = document.getElementById('histAvgCadence');
    this.histAvgHr = document.getElementById('histAvgHr');
    this.histMaxHr = document.getElementById('histMaxHr');

    this.histHrZoneSection = document.getElementById('histHrZoneSection');
    this.histHrZoneSummary = document.getElementById('histHrZoneSummary');
    this.histHrZoneDistBar = document.getElementById('histHrZoneDistBar');
    this.histHrZoneDistLegend = document.getElementById('histHrZoneDistLegend');

    this.btnRefreshHistory = document.getElementById('btnRefreshHistory');
    this.btnOpenActivitiesDir = document.getElementById('btnOpenActivitiesDir');
    this.btnOpenSelectedGpx = document.getElementById('btnOpenSelectedGpx');
    this.btnDeleteSelectedSession = document.getElementById('btnDeleteSelectedSession');

    // ボタンリスナー
    if (this.btnRefreshHistory) {
      this.btnRefreshHistory.addEventListener('click', () => this.refreshList());
    }
    if (this.btnOpenActivitiesDir) {
      this.btnOpenActivitiesDir.addEventListener('click', () => {
        this.postMessage('openActivitiesFolder');
      });
    }
    if (this.btnOpenSelectedGpx) {
      this.btnOpenSelectedGpx.addEventListener('click', () => {
        if (this.selectedSession && this.selectedSession.GpxFileName) {
          this.postMessage('openLogFolder', { path: this.selectedSession.GpxFileName });
        }
      });
    }
    if (this.btnDeleteSelectedSession) {
      this.btnDeleteSelectedSession.addEventListener('click', () => {
        if (!this.selectedSession) return;
        if (confirm(`選択中の走行ログ (${this.selectedSession.FileName}) を削除しますか？`)) {
          this.postMessage('deleteHistory', { fileName: this.selectedSession.FileName });
        }
      });
    }

    // Leaflet マップ初期化
    this.mapSize = 8192;
    this.initMap();

    // Chart.js チャート初期化
    this.initChart();

    // ルート全体表示ボタン
    const btnFitRoute = document.getElementById('btnFitHistoryRoute');
    if (btnFitRoute) {
      btnFitRoute.addEventListener('click', () => {
        this.fitRouteBounds();
      });
    }

    // 自動追従ON/OFFボタン (リアルタイムマップ追跡と同様の操作感)
    this.btnFollowHistoryPoint = document.getElementById('btnFollowHistoryPoint');
    if (this.btnFollowHistoryPoint) {
      this.btnFollowHistoryPoint.addEventListener('click', () => {
        this.followPoint = !this.followPoint;
        this.updateFollowButton();
      });
      this.updateFollowButton();
    }

    // 初回履歴読み込み
    this.refreshList();
  }

  updateFollowButton() {
    if (!this.btnFollowHistoryPoint) return;
    this.btnFollowHistoryPoint.classList.toggle('active', this.followPoint);
    this.btnFollowHistoryPoint.textContent = this.followPoint ? '📍 自動追従: ON' : '📍 自動追従: OFF';
  }

  setHrConfig(maxHr, restingHr) {
    if (maxHr) this.maxHeartRate = maxHr;
    if (restingHr) this.restingHeartRate = restingHr;
    if (this.currentPoints && this.currentPoints.length > 0) {
      this.renderHrZoneDistribution(this.currentPoints);
    }
  }

  postMessage(action, payload = {}) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage({ action, ...payload });
    } else {
      console.log('[DevWebMessage]', action, payload);
    }
  }

  initMap() {
    const mapEl = document.getElementById('historyMapView');
    if (!mapEl) return;

    // Forza Horizon 6 8192x8192 マップ (Leaflet CRS.Simple 平面直交座標系)
    this.map = L.map('historyMapView', {
      crs: L.CRS.Simple,
      minZoom: -3,
      maxZoom: 3,
      zoomControl: true,
      attributionControl: false
    });

    const fullBounds = [[0, 0], [this.mapSize, this.mapSize]];
    const fullMapUrl = 'https://maps.local/fh6_japan_map_full_8192x8192.jpg';
    const imageOverlay = L.imageOverlay(fullMapUrl, fullBounds, {
      errorOverlayUrl: 'https://maps.local/fh6_japan_map_cropped_4608x5632.jpg'
    });
    imageOverlay.addTo(this.map);

    this.map.fitBounds(fullBounds);
    this.map.setMaxBounds([[-1000, -1000], [this.mapSize + 1000, this.mapSize + 1000]]);

    // コンテナリサイズ監視 (大画面やリサイズ時に即座に地図描画領域を更新)
    if (window.ResizeObserver) {
      const resizeObserver = new ResizeObserver(() => {
        if (this.map) {
          this.map.invalidateSize();
        }
      });
      resizeObserver.observe(mapEl);
    }
  }

  // リアルタイム追跡・キャリブレーションで設定された原点・スケール・Z反転の反映
  setMapConfig(config) {
    if (!config) return;
    let changed = false;
    if (typeof config.mapOriginX !== 'undefined') {
      const val = typeof config.mapOriginX === 'number' ? config.mapOriginX : (parseFloat(config.mapOriginX) || 0.0);
      if (Math.abs(this.mapOriginX - val) > 0.001) { this.mapOriginX = val; changed = true; }
    }
    if (typeof config.mapOriginZ !== 'undefined') {
      const val = typeof config.mapOriginZ === 'number' ? config.mapOriginZ : (parseFloat(config.mapOriginZ) || 0.0);
      if (Math.abs(this.mapOriginZ - val) > 0.001) { this.mapOriginZ = val; changed = true; }
    }
    if (typeof config.mapScale !== 'undefined') {
      const val = typeof config.mapScale === 'number' ? config.mapScale : (parseFloat(config.mapScale) || 0.252);
      if (Math.abs(this.mapScale - val) > 0.0005) { this.mapScale = val; changed = true; }
    }
    if (typeof config.invertZ !== 'undefined') {
      const val = Boolean(config.invertZ);
      if (this.invertZ !== val) { this.invertZ = val; changed = true; }
    }
    // リアルタイム追跡の設定が更新されたら、表示中の軌跡も最新の縮尺・位置に即座に追従再描画
    if (changed && this.currentPoints && this.currentPoints.length > 0) {
      this.redrawTrack(false);
    }
  }

  // 軌跡の再描画 (縮尺やオフセットの微調整時に即座に反映)
  redrawTrack(shouldFitBounds = false) {
    if (!this.map || !this.currentPoints || this.currentPoints.length === 0) return;

    if (this.trackPolyline) this.map.removeLayer(this.trackPolyline);
    this.hideHoverPointOnMap();

    const segments = [];
    let currentSeg = [];

    for (let i = 0; i < this.currentPoints.length; i++) {
      const pt = this.currentPoints[i];
      const lat = pt.Latitude ?? pt.lat;
      const lon = pt.Longitude ?? pt.lon;

      let px, py;
      if (lat !== undefined && lon !== undefined && (lat !== 0 || lon !== 0)) {
        const pixel = this.gpsToPixel(lat, lon);
        px = pixel.px;
        py = pixel.py;
      } else {
        px = pt.PixelX ?? pt.pixelX ?? 4096;
        py = pt.PixelY ?? pt.pixelY ?? 4096;
      }

      const latLng = this.pixelToLatLng(px, py);
      const isTeleportFlag = (pt.IsTeleport === true || pt.isTeleport === true);

      if (currentSeg.length > 0) {
        const last = currentSeg[currentSeg.length - 1];
        const dist = Math.hypot(latLng.lat - last.lat, latLng.lng - last.lng);

        // リアルタイム追跡(map.js)と同一のファストトラベル判定 (45px以上のジャンプまたはテレポートフラグ)
        const isJump = isTeleportFlag || (dist >= 45.0);

        if (isJump) {
          // 移動前の軌跡セグメントを保存し、新しいセグメントを開始（直線を描かない）
          segments.push(currentSeg);
          currentSeg = [latLng];
        } else {
          currentSeg.push(latLng);
        }
      } else {
        currentSeg.push(latLng);
      }
    }

    if (currentSeg.length > 0) {
      segments.push(currentSeg);
    }

    if (segments.length === 0) return;

    // Leaflet L.polyline は配列の配列（セグメントリスト）を渡すとセグメント間を繋がずに描画
    this.trackPolyline = L.polyline(segments, {
      color: '#06b6d4',
      weight: 4,
      opacity: 0.95,
      lineJoin: 'round',
      smoothFactor: 1.0
    }).addTo(this.map);

    if (shouldFitBounds) {
      setTimeout(() => {
        this.fitRouteBounds();
      }, 80);
    }
  }

  // 擬似GPS座標 (緯度, 経度) をリアルタイム追跡と完全に同じ計算式で 8192ピクセル座標にマッピング
  gpsToPixel(lat, lon) {
    const worldX = (lon - 138.7274) * 91287.0;
    const worldZ = (35.3606 - lat) * 111139.0;
    const px = (this.mapSize / 2.0) + (worldX - this.mapOriginX) * this.mapScale;
    const py = this.invertZ
      ? (this.mapSize / 2.0) - (worldZ - this.mapOriginZ) * this.mapScale
      : (this.mapSize / 2.0) + (worldZ - this.mapOriginZ) * this.mapScale;
    return {
      px: Math.max(0, Math.min(this.mapSize, px)),
      py: Math.max(0, Math.min(this.mapSize, py))
    };
  }

  // 8192ピクセル座標 (左上 0,0 〜 右下 8192,8192) を Leaflet CRS.Simple に変換
  pixelToLatLng(px, py) {
    const lat = this.mapSize - py;
    const lng = px;
    return L.latLng(lat, lng);
  }

  fitRouteBounds() {
    if (this.map && this.trackPolyline) {
      const bounds = this.trackPolyline.getBounds();
      if (bounds.isValid()) {
        this.map.invalidateSize();
        this.map.fitBounds(bounds, { padding: [40, 40], maxZoom: 2 });
      }
    }
  }

  initChart() {
    const ctx = document.getElementById('historyChart');
    if (!ctx) return;

    const eleGradient = ctx.getContext('2d').createLinearGradient(0, 0, 0, 240);
    eleGradient.addColorStop(0, 'rgba(16, 185, 129, 0.28)');
    eleGradient.addColorStop(1, 'rgba(16, 185, 129, 0.0)');

    this.chart = new Chart(ctx, {
      type: 'line',
      data: {
        labels: [],
        datasets: [
          {
            label: '標高 (m)',
            data: [],
            yAxisID: 'yElevation',
            borderColor: '#10b981',
            borderWidth: 2,
            fill: true,
            backgroundColor: eleGradient,
            tension: 0.2,
            pointRadius: 0,
            pointHoverRadius: 4,
            order: 4
          },
          {
            label: '速度 (km/h)',
            data: [],
            yAxisID: 'ySpeed',
            borderColor: '#06b6d4',
            borderWidth: 1.8,
            fill: false,
            tension: 0.2,
            pointRadius: 0,
            pointHoverRadius: 4,
            order: 2
          },
          {
            label: '目標速度 (km/h)',
            data: [],
            yAxisID: 'ySpeed',
            borderColor: '#a855f7',
            borderWidth: 1.5,
            borderDash: [4, 4],
            fill: false,
            tension: 0.2,
            pointRadius: 0,
            pointHoverRadius: 4,
            order: 3
          },
          {
            label: 'パワー (W)',
            data: [],
            yAxisID: 'yPower',
            borderColor: '#f59e0b',
            borderWidth: 1.8,
            fill: false,
            tension: 0.2,
            pointRadius: 0,
            pointHoverRadius: 4,
            order: 1
          },
          {
            label: '心拍数 (bpm)',
            data: [],
            yAxisID: 'yHeartRate',
            borderColor: '#f43f5e',
            borderWidth: 1.8,
            fill: false,
            tension: 0.2,
            pointRadius: 0,
            pointHoverRadius: 4,
            order: 4
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: { duration: 250 },
        interaction: {
          mode: 'index',
          intersect: false
        },
        onHover: (event, elements) => {
          if (elements && elements.length > 0) {
            const index = elements[0].index;
            this.showHoverPointOnMap(index);
          } else {
            this.hideHoverPointOnMap();
          }
        },
        scales: {
          x: {
            grid: { color: 'rgba(255, 255, 255, 0.05)' },
            ticks: {
              color: '#94a3b8',
              font: { family: 'Roboto Mono', size: 10 },
              maxTicksLimit: 10
            },
            title: {
              display: true,
              text: '走行距離 (km)',
              color: '#64748b',
              font: { size: 11, weight: 'bold' }
            }
          },
          yElevation: {
            type: 'linear',
            display: true,
            position: 'left',
            grid: { color: 'rgba(255, 255, 255, 0.05)' },
            ticks: {
              color: '#10b981',
              font: { family: 'Roboto Mono', size: 10 }
            },
            title: {
              display: true,
              text: '標高 (m)',
              color: '#10b981',
              font: { size: 11, weight: 'bold' }
            }
          },
          ySpeed: {
            type: 'linear',
            display: true,
            position: 'right',
            min: 0,
            suggestedMax: 60,
            grid: { drawOnChartArea: false },
            ticks: {
              color: '#06b6d4',
              font: { family: 'Roboto Mono', size: 10 }
            },
            title: {
              display: true,
              text: '速度 (km/h)',
              color: '#06b6d4',
              font: { size: 11, weight: 'bold' }
            }
          },
          yPower: {
            type: 'linear',
            display: true,
            position: 'right',
            min: 0,
            suggestedMax: 300,
            grid: { drawOnChartArea: false },
            ticks: {
              color: '#f59e0b',
              font: { family: 'Roboto Mono', size: 10 }
            },
            title: {
              display: true,
              text: 'パワー (W)',
              color: '#f59e0b',
              font: { size: 11, weight: 'bold' }
            }
          },
          yHeartRate: {
            type: 'linear',
            display: true,
            position: 'right',
            min: 40,
            suggestedMax: 200,
            grid: { drawOnChartArea: false },
            ticks: {
              color: '#f43f5e',
              font: { family: 'Roboto Mono', size: 10 }
            },
            title: {
              display: true,
              text: '心拍 (bpm)',
              color: '#f43f5e',
              font: { size: 11, weight: 'bold' }
            }
          }
        },
        plugins: {
          legend: {
            display: true,
            position: 'top',
            align: 'end',
            labels: {
              color: '#cbd5e1',
              font: { family: 'Inter, sans-serif', size: 11, weight: '600' },
              usePointStyle: true,
              boxWidth: 8,
              padding: 12
            }
          },
          tooltip: {
            backgroundColor: 'rgba(18, 24, 36, 0.95)',
            titleFont: { family: 'Roboto Mono', size: 11, weight: 'bold' },
            bodyFont: { family: 'Roboto Mono', size: 10 },
            borderColor: 'rgba(255, 255, 255, 0.12)',
            borderWidth: 1,
            padding: 8,
            callbacks: {
              title: (items) => {
                if (!items.length) return '';
                return `距離: ${items[0].label}`;
              },
              label: (context) => {
                const val = context.parsed.y;
                if (val === null || val === undefined) return '';
                if (context.datasetIndex === 0) return ` 標高: ${val.toFixed(1)} m`;
                if (context.datasetIndex === 1) return ` 速度: ${val.toFixed(1)} km/h`;
                if (context.datasetIndex === 2) return ` 目標速度: ${val.toFixed(1)} km/h`;
                if (context.datasetIndex === 3) return ` パワー: ${Math.round(val)} W`;
                if (context.datasetIndex === 4) {
                  const hrVal = Math.round(val);
                  let zText = '';
                  if (hrVal > 0 && window.getHrZone) {
                    const z = window.getHrZone(hrVal, this.maxHeartRate, this.restingHeartRate);
                    if (z && z.zone > 0) zText = ` (${z.shortName} ${z.name})`;
                  }
                  return ` 心拍数: ${hrVal} bpm${zText}`;
                }
                return ` ${context.dataset.label}: ${val}`;
              }
            }
          }
        }
      }
    });

    ctx.addEventListener('mouseleave', () => {
      this.hideHoverPointOnMap();
    });
  }

  refreshList() {
    if (this.listContainer) {
      this.listContainer.innerHTML = '<div class="history-loading">履歴を読み込み中...</div>';
    }
    this.postMessage('getHistoryList');
  }

  handleHistoryList(list) {
    this.historyList = list || [];
    if (!this.listContainer) return;

    if (this.historyList.length === 0) {
      this.listContainer.innerHTML = '<div class="history-empty">保存された走行ログはありません</div>';
      this.showEmptyState();
      return;
    }

    this.listContainer.innerHTML = '';
    this.historyList.forEach((item, index) => {
      const card = document.createElement('div');
      card.className = 'history-card';
      if (this.selectedSession && this.selectedSession.FileName === item.FileName) {
        card.classList.add('active');
      }

      // 日時フォーマット
      const dateStr = this.formatDate(item.StartTime);
      const movingTimeStr = this.formatSeconds(item.MovingDurationSec);

      card.innerHTML = `
        <div class="h-card-header">
          <span class="h-card-date">${dateStr}</span>
          <span class="h-card-time">⏱ ${movingTimeStr}</span>
        </div>
        <div class="h-card-stats">
          <div class="h-stat-mini">
            <span class="h-stat-mini-label">距離</span>
            <span class="h-stat-mini-val text-orange">${item.DistanceKm.toFixed(2)} km</span>
          </div>
          <div class="h-stat-mini">
            <span class="h-stat-mini-label">獲得標高</span>
            <span class="h-stat-mini-val text-green">+${Math.round(item.ElevationGainM)} m</span>
          </div>
          <div class="h-stat-mini">
            <span class="h-stat-mini-label">平均パワー</span>
            <span class="h-stat-mini-val text-amber">${Math.round(item.AvgPowerWatts)} W</span>
          </div>
        </div>
      `;

      card.addEventListener('click', () => {
        this.selectSession(item);
      });

      this.listContainer.appendChild(card);
    });

    // 選択されていない場合、先頭（最新）を自動選択
    if (!this.selectedSession && this.historyList.length > 0) {
      this.selectSession(this.historyList[0]);
    }
  }

  selectSession(item) {
    this.selectedSession = item;
    this.hideHoverPointOnMap();

    // リストの選択ハイライト更新
    const cards = this.listContainer.querySelectorAll('.history-card');
    cards.forEach((c, idx) => {
      if (this.historyList[idx] && this.historyList[idx].FileName === item.FileName) {
        c.classList.add('active');
      } else {
        c.classList.remove('active');
      }
    });

    // 詳細表示
    if (this.emptyState) this.emptyState.classList.add('hidden');
    if (this.detailPane) this.detailPane.classList.remove('hidden');

    // サマリースタッツの即時反映
    if (this.histTitleDate) this.histTitleDate.textContent = this.formatDate(item.StartTime);
    if (this.histSessionIdBadge) this.histSessionIdBadge.textContent = `ID: ${item.SessionId || item.FileName.replace('.json', '')}`;
    if (this.histMovingTime) this.histMovingTime.textContent = this.formatSeconds(item.MovingDurationSec);
    if (this.histTotalTime) this.histTotalTime.textContent = `総時間: ${this.formatSeconds(item.TotalDurationSec)}`;
    if (this.histDistance) this.histDistance.textContent = `${item.DistanceKm.toFixed(2)} km`;
    if (this.histAvgSpeed) this.histAvgSpeed.textContent = `平均: ${item.AvgSpeedKmh.toFixed(1)} km/h`;
    if (this.histElevationGain) this.histElevationGain.textContent = `+${Math.round(item.ElevationGainM)} m`;
    if (this.histMaxSpeed) this.histMaxSpeed.textContent = `最高速: ${item.MaxSpeedKmh.toFixed(1)} km/h`;
    if (this.histAvgPower) this.histAvgPower.textContent = `${Math.round(item.AvgPowerWatts)} W`;
    if (this.histMaxPower) this.histMaxPower.textContent = `NP: ${Math.round(item.NormalizedPowerWatts)}W / Max: ${Math.round(item.MaxPowerWatts)}W`;
    if (this.histCalories) this.histCalories.textContent = `${Math.round(item.CaloriesKcal)} kcal`;
    if (this.histAvgCadence) this.histAvgCadence.textContent = `平均ケイデンス: ${Math.round(item.AvgCadence)} rpm`;

    // 心拍スタッツ
    const avgHr = Math.round(item.AvgHeartRate || 0);
    const maxHr = Math.round(item.MaxHeartRate || 0);
    if (this.histAvgHr) {
      this.histAvgHr.textContent = avgHr > 0 ? `${avgHr} bpm` : '-- bpm';
    }
    if (this.histMaxHr) {
      if (maxHr > 0) {
        const z = window.getHrZone ? window.getHrZone(maxHr, this.maxHeartRate, this.restingHeartRate) : null;
        this.histMaxHr.textContent = `Max: ${maxHr} bpm${z && z.zone > 0 ? ` (${z.shortName})` : ''}`;
      } else {
        this.histMaxHr.textContent = 'Max: -- bpm';
      }
    }

    // 詳細データ（トラックポイント配列）を取得要求
    this.postMessage('getHistoryDetail', { fileName: item.FileName });
  }

  handleHistoryDetail(data, meta) {
    if (meta) {
      this.setMapConfig(meta);
    }
    if (!data || !data.points) return;
    const points = data.points;
    this.currentPoints = points;

    // 1. 地図にトラックポイント描画 (リアルタイムマップ追跡の位置合わせと完全に一致)
    this.redrawTrack(true);

    // 2. 心拍ゾーン分布の計算・描画
    this.renderHrZoneDistribution(points);

    // 2. 過去グラフ（Chart.js）描画
    if (this.chart) {
      const labels = [];
      const dataElevation = [];
      const dataSpeed = [];
      const dataTargetSpeed = [];
      const dataPower = [];
      const dataHeartRate = [];
      this.chartPoints = [];

      // 300点程度にダウンサンプリング（点が多すぎる場合のスムーズ表示）
      const step = Math.max(1, Math.ceil(points.length / 300));

      for (let i = 0; i < points.length; i += step) {
        const pt = points[i];
        this.chartPoints.push(pt);
        const distKm = pt.DistanceKm ?? pt.distKm ?? 0;
        labels.push(`${distKm.toFixed(2)} km`);
        dataElevation.push(Math.round((pt.Elevation ?? pt.ele ?? 0) * 10) / 10);
        dataSpeed.push(Math.round((pt.SpeedKmh ?? pt.speed ?? 0) * 10) / 10);
        dataTargetSpeed.push(Math.round((pt.TargetSpeedKmh ?? pt.targetSpeed ?? 0) * 10) / 10);
        dataPower.push(Math.round(pt.Power ?? pt.power ?? 0));
        dataHeartRate.push(Math.round(pt.HeartRate ?? pt.hr ?? 0));
      }

      this.chart.data.labels = labels;
      this.chart.data.datasets[0].data = dataElevation;
      this.chart.data.datasets[1].data = dataSpeed;
      this.chart.data.datasets[2].data = dataTargetSpeed;
      this.chart.data.datasets[3].data = dataPower;
      this.chart.data.datasets[4].data = dataHeartRate;
      this.chart.update();

      setTimeout(() => {
        if (this.chart) this.chart.resize();
      }, 50);
    }
  }

  // 走行プロファイル推移のホバー位置に対応するポイントをマップ上にハイライト表示
  showHoverPointOnMap(index) {
    if (!this.map || !this.chartPoints || !this.chartPoints[index]) return;
    const pt = this.chartPoints[index];
    const lat = pt.Latitude ?? pt.lat;
    const lon = pt.Longitude ?? pt.lon;

    let px, py;
    if (lat !== undefined && lon !== undefined && (lat !== 0 || lon !== 0)) {
      const pixel = this.gpsToPixel(lat, lon);
      px = pixel.px;
      py = pixel.py;
    } else {
      px = pt.PixelX ?? pt.pixelX ?? 4096;
      py = pt.PixelY ?? pt.pixelY ?? 4096;
    }
    const latLng = this.pixelToLatLng(px, py);

    if (!this.hoverMarker) {
      this.hoverMarker = L.layerGroup().addTo(this.map);
      this.hoverOuter = L.circleMarker(latLng, {
        radius: 13,
        fillColor: '#f59e0b',
        fillOpacity: 0.25,
        color: '#f59e0b',
        weight: 1.5,
        interactive: false
      });
      this.hoverDot = L.circleMarker(latLng, {
        radius: 6,
        fillColor: '#f59e0b',
        fillOpacity: 1.0,
        color: '#ffffff',
        weight: 2,
        interactive: false
      });
      this.hoverMarker.addLayer(this.hoverOuter);
      this.hoverMarker.addLayer(this.hoverDot);
    } else {
      this.hoverOuter.setLatLng(latLng);
      this.hoverDot.setLatLng(latLng);
      if (!this.map.hasLayer(this.hoverMarker)) {
        this.hoverMarker.addTo(this.map);
      }
    }

    const distKm = (pt.DistanceKm ?? pt.distKm ?? 0).toFixed(2);
    const ele = Math.round(pt.Elevation ?? pt.ele ?? 0);
    const spd = (pt.SpeedKmh ?? pt.speed ?? 0).toFixed(1);
    const pwr = Math.round(pt.Power ?? pt.power ?? 0);
    const hr = Math.round(pt.HeartRate ?? pt.hr ?? 0);
    let tooltipText = `📍 ${distKm}km | ${ele}m | ${spd}km/h | ${pwr}W`;
    if (hr > 0) {
      const z = window.getHrZone ? window.getHrZone(hr, this.maxHeartRate, this.restingHeartRate) : null;
      tooltipText += ` | ❤️${hr}${z && z.zone > 0 ? ` (${z.shortName})` : ''}`;
    }

    this.hoverDot.unbindTooltip();
    this.hoverDot.bindTooltip(tooltipText, {
      permanent: true,
      direction: 'top',
      offset: [0, -8],
      className: 'history-map-tooltip'
    }).openTooltip();

    // 自動追従が有効な場合、ホバー位置をマップの中心にスムーズに移動
    if (this.followPoint && this.map) {
      this.map.panTo(latLng, { animate: true, duration: 0.15 });
    }
  }

  // ホバーポイントの非表示
  hideHoverPointOnMap() {
    if (this.hoverMarker && this.map && this.map.hasLayer(this.hoverMarker)) {
      this.map.removeLayer(this.hoverMarker);
    }
  }

  handleHistoryDeleted(fileName, success) {
    if (success) {
      if (this.selectedSession && this.selectedSession.FileName === fileName) {
        this.selectedSession = null;
      }
      this.refreshList();
    } else {
      alert('ファイルの削除に失敗しました。');
    }
  }

  showEmptyState() {
    this.hideHoverPointOnMap();
    if (this.emptyState) this.emptyState.classList.remove('hidden');
    if (this.detailPane) this.detailPane.classList.add('hidden');
  }

  invalidateSize() {
    if (this.map) {
      setTimeout(() => {
        this.map.invalidateSize();
        if (this.trackPolyline) {
          const bounds = this.trackPolyline.getBounds();
          if (bounds.isValid()) {
            this.map.fitBounds(bounds, { padding: [40, 40], maxZoom: 2 });
          }
        }
      }, 50);
    }
    if (this.chart) {
      setTimeout(() => {
        this.chart.resize();
      }, 50);
    }
  }

  renderHrZoneDistribution(points) {
    if (!this.histHrZoneSection) return;

    if (!points || points.length === 0) {
      this.histHrZoneSection.classList.add('hidden');
      return;
    }

    // ゾーンごとの滞在時間（秒数）を集計
    const zoneSeconds = { 1: 0, 2: 0, 3: 0, 4: 0, 5: 0 };
    let totalHrSec = 0;
    let sumHr = 0;
    let hrCount = 0;
    let maxHrFound = 0;

    for (let i = 0; i < points.length; i++) {
      const pt = points[i];
      const hr = pt.HeartRate ?? pt.hr ?? 0;
      if (hr > 0) {
        let dt = 1;
        if (i > 0 && points[i - 1].Timestamp && pt.Timestamp) {
          const tPrev = new Date(points[i - 1].Timestamp).getTime();
          const tCur = new Date(pt.Timestamp).getTime();
          const diffSec = (tCur - tPrev) / 1000;
          if (diffSec > 0 && diffSec <= 5) {
            dt = diffSec;
          }
        }
        sumHr += hr;
        hrCount++;
        if (hr > maxHrFound) maxHrFound = hr;

        const z = window.getHrZone ? window.getHrZone(hr, this.maxHeartRate, this.restingHeartRate) : null;
        if (z && z.zone >= 1 && z.zone <= 5) {
          zoneSeconds[z.zone] += dt;
          totalHrSec += dt;
        }
      }
    }

    // 心拍データが無い場合は非表示
    if (totalHrSec <= 0 || hrCount === 0) {
      this.histHrZoneSection.classList.add('hidden');
      return;
    }

    this.histHrZoneSection.classList.remove('hidden');

    const avgHrCalc = Math.round(sumHr / hrCount);
    // スタッツカードが未設定だった場合は補完
    if (this.histAvgHr && (this.histAvgHr.textContent === '-- bpm' || !this.selectedSession?.AvgHeartRate)) {
      this.histAvgHr.textContent = `${avgHrCalc} bpm`;
    }
    if (this.histMaxHr && (this.histMaxHr.textContent === 'Max: -- bpm' || !this.selectedSession?.MaxHeartRate)) {
      const zMax = window.getHrZone ? window.getHrZone(maxHrFound, this.maxHeartRate, this.restingHeartRate) : null;
      this.histMaxHr.textContent = `Max: ${Math.round(maxHrFound)} bpm${zMax && zMax.zone > 0 ? ` (${zMax.shortName})` : ''}`;
    }

    // サマリー表示
    if (this.histHrZoneSummary) {
      this.histHrZoneSummary.textContent = `計測: ${this.formatSeconds(totalHrSec)} | 平均: ${avgHrCalc} bpm | 最大: ${Math.round(maxHrFound)} bpm`;
    }

    // ゾーン範囲一覧を取得
    const ranges = window.getHrZoneRanges ? window.getHrZoneRanges(this.maxHeartRate, this.restingHeartRate) : [
      { zone: 1, name: '回復', shortName: 'Z1', min: 60, max: 113, color: '#38bdf8' },
      { zone: 2, name: '有酸素', shortName: 'Z2', min: 114, max: 132, color: '#22c55e' },
      { zone: 3, name: 'テンポ', shortName: 'Z3', min: 133, max: 151, color: '#eab308' },
      { zone: 4, name: '乳酸閾値', shortName: 'Z4', min: 152, max: 170, color: '#f97316' },
      { zone: 5, name: '無酸素/最大', shortName: 'Z5', min: 171, max: 190, color: '#ef4444' }
    ];

    // 1. スタックバー生成
    if (this.histHrZoneDistBar) {
      let barHtml = '';
      ranges.forEach(r => {
        const sec = zoneSeconds[r.zone] || 0;
        const pct = (sec / totalHrSec) * 100;
        if (pct > 0) {
          barHtml += `<div class="hr-zone-dist-bar-seg zone-${r.zone}" style="width: ${pct.toFixed(2)}%;" title="${r.shortName} ${r.name}: ${pct.toFixed(1)}% (${this.formatSeconds(sec)})"></div>`;
        }
      });
      this.histHrZoneDistBar.innerHTML = barHtml;
    }

    // 2. 凡例グリッド生成
    if (this.histHrZoneDistLegend) {
      let legendHtml = '';
      ranges.forEach(r => {
        const sec = zoneSeconds[r.zone] || 0;
        const pct = (sec / totalHrSec) * 100;
        const rangeText = r.zone === 1 ? `< ${r.max + 1} bpm` : (r.zone === 5 ? `≥ ${r.min} bpm` : `${r.min} - ${r.max} bpm`);
        legendHtml += `
          <div class="hr-zone-legend-item">
            <div class="hr-zone-legend-header">
              <span class="hr-zone-dot zone-${r.zone}"></span>
              <span>${r.shortName} ${r.name}</span>
            </div>
            <span class="hr-zone-range">${rangeText}</span>
            <div class="hr-zone-legend-val">
              <span class="hr-zone-time">${this.formatSeconds(sec)}</span>
              <span class="hr-zone-pct">${pct.toFixed(1)}%</span>
            </div>
          </div>
        `;
      });
      this.histHrZoneDistLegend.innerHTML = legendHtml;
    }
  }

  formatDate(isoString) {
    if (!isoString) return '日付不明';
    try {
      const d = new Date(isoString);
      if (isNaN(d.getTime())) return isoString;
      const y = d.getFullYear();
      const m = String(d.getMonth() + 1).padStart(2, '0');
      const day = String(d.getDate()).padStart(2, '0');
      const h = String(d.getHours()).padStart(2, '0');
      const min = String(d.getMinutes()).padStart(2, '0');
      return `${y}/${m}/${day} ${h}:${min}`;
    } catch {
      return isoString;
    }
  }

  formatSeconds(sec) {
    if (!sec || sec < 0) return '00:00:00';
    const s = Math.floor(sec);
    const hrs = Math.floor(s / 3600);
    const mins = Math.floor((s % 3600) / 60);
    const secs = s % 60;
    return `${hrs.toString().padStart(2, '0')}:${mins.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
  }
}

window.SessionHistoryController = SessionHistoryController;
