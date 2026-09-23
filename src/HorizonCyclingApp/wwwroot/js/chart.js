/**
 * HorizonCycling Studio - Realtime Elevation Profile Chart (Chart.js)
 * Supports Dual View: Recent 200m Zoom & Full Route History
 */

class ElevationProfileChart {
  constructor(canvasId) {
    this.canvasId = canvasId;
    this.chart = null;

    // 全走行履歴データ [{ distKm, distM, ele }]
    this.history = [];

    // 表示モード: 'recent' (直近200m) または 'all' (全走行距離)
    this.viewMode = 'recent';

    // サンプリング制御
    this.lastRecordedDistM = -999.0;
    this.minSampleIntervalM = 3.0; // 3mごとに1点サンプリング

    this.currentElevation = 0.0;
    this.currentDistanceKm = 0.0;
    this.currentDisplayedPoints = [];
  }

  init() {
    const ctx = document.getElementById(this.canvasId);
    if (!ctx) return;

    // グラデーション背景の作成
    const gradient = ctx.getContext('2d').createLinearGradient(0, 0, 0, 300);
    gradient.addColorStop(0, 'rgba(16, 185, 129, 0.45)');
    gradient.addColorStop(1, 'rgba(16, 185, 129, 0.0)');

    this.chart = new Chart(ctx, {
      type: 'line',
      data: {
        labels: [],
        datasets: [{
          label: '標高 (m)',
          data: [],
          borderColor: '#10b981',
          borderWidth: 2.5,
          fill: true,
          backgroundColor: gradient,
          tension: 0.25,
          pointRadius: 0,
          pointHoverRadius: 5
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: { duration: 0 }, // リアルタイム描画のためアニメーションオフ
        scales: {
          x: {
            grid: { color: 'rgba(255, 255, 255, 0.05)' },
            ticks: {
              color: '#94a3b8',
              font: { family: 'Roboto Mono', size: 11 },
              maxTicksLimit: 8
            },
            title: {
              display: true,
              text: '直近 200m (現在地: 右端)',
              color: '#64748b',
              font: { size: 11, weight: 'bold' }
            }
          },
          y: {
            grid: { color: 'rgba(255, 255, 255, 0.05)' },
            ticks: {
              color: '#94a3b8',
              font: { family: 'Roboto Mono', size: 11 }
            },
            title: {
              display: true,
              text: '標高 (m)',
              color: '#64748b',
              font: { size: 11, weight: 'bold' }
            }
          }
        },
        plugins: {
          legend: { display: false },
          tooltip: {
            backgroundColor: 'rgba(18, 24, 36, 0.95)',
            titleFont: { family: 'Roboto Mono', size: 12 },
            bodyFont: { family: 'Roboto Mono', size: 12 },
            borderColor: 'rgba(16, 185, 129, 0.3)',
            borderWidth: 1,
            callbacks: {
              title: (items) => {
                if (!items.length) return '';
                const idx = items[0].dataIndex;
                const pt = this.currentDisplayedPoints?.[idx];
                if (pt) {
                  return `距離: ${pt.distKm.toFixed(2)} km (${Math.round(pt.distM)} m)`;
                }
                return items[0].label;
              },
              label: (context) => `標高: ${context.parsed.y.toFixed(1)} m`
            }
          }
        }
      }
    });
  }

  // 表示モードの切り替え ('recent' | 'all')
  setViewMode(mode) {
    if (this.viewMode === mode) return;
    this.viewMode = mode;
    this.renderChart();
  }

  // テレメトリ受信ごとの更新
  update(currentElevation, totalDistanceKm) {
    this.currentElevation = currentElevation;
    this.currentDistanceKm = totalDistanceKm;

    const currentDistM = totalDistanceKm * 1000.0;

    // サンプリング（初回または一定距離以上進んだら記録）
    if (this.history.length === 0 || Math.abs(currentDistM - this.lastRecordedDistM) >= this.minSampleIntervalM) {
      this.history.push({
        distKm: totalDistanceKm,
        distM: currentDistM,
        ele: currentElevation
      });
      this.lastRecordedDistM = currentDistM;

      // メモリ保護（20000点超えで半分に間引き）
      if (this.history.length > 20000) {
        this.history = this.history.filter((_, i) => i % 2 === 0);
      }
    }

    this.renderChart();
  }

  renderChart() {
    if (!this.chart) return;

    const currentDistM = this.currentDistanceKm * 1000.0;
    let rawPoints = [];

    if (this.viewMode === 'recent') {
      // 直近200mモード
      const startM = Math.max(0, currentDistM - 200.0);
      rawPoints = this.history.filter(pt => pt.distM >= startM);

      // 最新地点を末尾に付加（最新データが履歴と若干異なる場合）
      if (rawPoints.length === 0 || Math.abs(rawPoints[rawPoints.length - 1].distM - currentDistM) > 0.5) {
        rawPoints.push({
          distKm: this.currentDistanceKm,
          distM: currentDistM,
          ele: this.currentElevation
        });
      }
    } else {
      // 全走行距離モード
      if (this.history.length > 400) {
        // 400点を超える場合は滑らかにダウンサンプリング
        const step = Math.ceil(this.history.length / 300);
        rawPoints = this.history.filter((_, idx) => idx % step === 0);
      } else {
        rawPoints = [...this.history];
      }

      // 最新地点を末尾に付加
      if (rawPoints.length === 0 || Math.abs(rawPoints[rawPoints.length - 1].distM - currentDistM) > 0.5) {
        rawPoints.push({
          distKm: this.currentDistanceKm,
          distM: currentDistM,
          ele: this.currentElevation
        });
      }
    }

    this.currentDisplayedPoints = rawPoints;

    // ラベルとデータの構築
    const labels = [];
    const data = [];

    for (let i = 0; i < rawPoints.length; i++) {
      const pt = rawPoints[i];
      if (this.viewMode === 'recent') {
        const offsetM = Math.round(pt.distM - currentDistM);
        labels.push(offsetM === 0 ? '0m (現在)' : `${offsetM}m`);
      } else {
        labels.push(`${pt.distKm.toFixed(2)} km`);
      }
      data.push(Math.round(pt.ele * 10) / 10);
    }

    this.chart.data.labels = labels;
    this.chart.data.datasets[0].data = data;
    this.chart.options.scales.x.title.text = (this.viewMode === 'recent' 
      ? '直近 200m (現在地: 右端)' 
      : '総走行距離 (km)');

    this.chart.update('none');
  }

  reset() {
    this.history = [];
    this.lastRecordedDistM = -999.0;
    this.currentDisplayedPoints = [];
    if (this.chart) {
      this.chart.data.labels = [];
      this.chart.data.datasets[0].data = [];
      this.chart.update();
    }
  }
}

window.ElevationProfileChart = ElevationProfileChart;
