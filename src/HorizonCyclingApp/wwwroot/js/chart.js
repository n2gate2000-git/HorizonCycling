/**
 * HorizonCycling Studio - Realtime Ride Profile Chart (Chart.js)
 * Supports Dual View: Recent 200m Zoom & Full Route History
 * Metrics: Elevation, Speed, Target Speed, Power
 */

class ElevationProfileChart {
  constructor(canvasId) {
    this.canvasId = canvasId;
    this.chart = null;

    // 全走行履歴データ [{ distKm, distM, ele, speed, targetSpeed, power }]
    this.history = [];

    // 表示モード: 'recent' (直近200m) または 'all' (全走行距離)
    this.viewMode = 'recent';

    // サンプリング制御
    this.lastRecordedDistM = -999.0;
    this.minSampleIntervalM = 3.0; // 3mごとに1点サンプリング

    this.currentElevation = 0.0;
    this.currentDistanceKm = 0.0;
    this.currentSpeed = 0.0;
    this.currentTargetSpeed = 0.0;
    this.currentPower = 0.0;

    this.currentDisplayedPoints = [];
  }

  init() {
    const ctx = document.getElementById(this.canvasId);
    if (!ctx) return;

    // 標高用グラデーション背景
    const eleGradient = ctx.getContext('2d').createLinearGradient(0, 0, 0, 300);
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
            borderWidth: 2.2,
            fill: true,
            backgroundColor: eleGradient,
            tension: 0.25,
            pointRadius: 0,
            pointHoverRadius: 4,
            order: 4
          },
          {
            label: '速度 (km/h)',
            data: [],
            yAxisID: 'ySpeed',
            borderColor: '#06b6d4',
            borderWidth: 2.0,
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
            borderWidth: 1.8,
            borderDash: [5, 4],
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
            borderWidth: 2.0,
            fill: false,
            tension: 0.2,
            pointRadius: 0,
            pointHoverRadius: 4,
            order: 1
          }
        ]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: { duration: 0 }, // リアルタイム描画のためアニメーションオフ
        interaction: {
          mode: 'index',
          intersect: false
        },
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
          yElevation: {
            type: 'linear',
            display: true,
            position: 'left',
            grid: { color: 'rgba(255, 255, 255, 0.06)' },
            ticks: {
              color: '#10b981',
              font: { family: 'Roboto Mono', size: 11 }
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
            grid: { drawOnChartArea: false }, // 標高のグリッドと重複しないように
            ticks: {
              color: '#06b6d4',
              font: { family: 'Roboto Mono', size: 11 }
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
              font: { family: 'Roboto Mono', size: 11 }
            },
            title: {
              display: true,
              text: 'パワー (W)',
              color: '#f59e0b',
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
              padding: 14
            }
          },
          tooltip: {
            backgroundColor: 'rgba(18, 24, 36, 0.95)',
            titleFont: { family: 'Roboto Mono', size: 12, weight: 'bold' },
            bodyFont: { family: 'Roboto Mono', size: 11 },
            borderColor: 'rgba(255, 255, 255, 0.12)',
            borderWidth: 1,
            padding: 10,
            callbacks: {
              title: (items) => {
                if (!items.length) return '';
                const idx = items[0].dataIndex;
                const pt = this.currentDisplayedPoints?.[idx];
                if (pt) {
                  return `地点: ${pt.distKm.toFixed(2)} km (${Math.round(pt.distM)} m)`;
                }
                return items[0].label;
              },
              label: (context) => {
                const val = context.parsed.y;
                if (val === null || val === undefined) return '';
                if (context.datasetIndex === 0) {
                  return ` 標高: ${val.toFixed(1)} m`;
                } else if (context.datasetIndex === 1) {
                  return ` 速度: ${val.toFixed(1)} km/h`;
                } else if (context.datasetIndex === 2) {
                  return ` 目標速度: ${val.toFixed(1)} km/h`;
                } else if (context.datasetIndex === 3) {
                  return ` パワー: ${Math.round(val)} W`;
                }
                return ` ${context.dataset.label}: ${val}`;
              }
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
  update(currentElevation, totalDistanceKm, speed = 0, targetSpeed = 0, power = 0) {
    this.currentElevation = currentElevation;
    this.currentDistanceKm = totalDistanceKm;
    this.currentSpeed = Math.max(0, speed);
    this.currentTargetSpeed = Math.max(0, targetSpeed);
    this.currentPower = Math.max(0, power);

    const currentDistM = totalDistanceKm * 1000.0;

    // サンプリング（初回または一定距離以上進んだら記録）
    if (this.history.length === 0 || Math.abs(currentDistM - this.lastRecordedDistM) >= this.minSampleIntervalM) {
      this.history.push({
        distKm: totalDistanceKm,
        distM: currentDistM,
        ele: currentElevation,
        speed: this.currentSpeed,
        targetSpeed: this.currentTargetSpeed,
        power: this.currentPower
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
          ele: this.currentElevation,
          speed: this.currentSpeed,
          targetSpeed: this.currentTargetSpeed,
          power: this.currentPower
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
          ele: this.currentElevation,
          speed: this.currentSpeed,
          targetSpeed: this.currentTargetSpeed,
          power: this.currentPower
        });
      }
    }

    this.currentDisplayedPoints = rawPoints;

    // ラベルとデータの構築
    const labels = [];
    const dataElevation = [];
    const dataSpeed = [];
    const dataTargetSpeed = [];
    const dataPower = [];

    for (let i = 0; i < rawPoints.length; i++) {
      const pt = rawPoints[i];
      if (this.viewMode === 'recent') {
        const offsetM = Math.round(pt.distM - currentDistM);
        labels.push(offsetM === 0 ? '0m (現在)' : `${offsetM}m`);
      } else {
        labels.push(`${pt.distKm.toFixed(2)} km`);
      }
      dataElevation.push(Math.round(pt.ele * 10) / 10);
      dataSpeed.push(Math.round((pt.speed || 0) * 10) / 10);
      dataTargetSpeed.push(Math.round((pt.targetSpeed || 0) * 10) / 10);
      dataPower.push(Math.round(pt.power || 0));
    }

    this.chart.data.labels = labels;
    this.chart.data.datasets[0].data = dataElevation;
    this.chart.data.datasets[1].data = dataSpeed;
    this.chart.data.datasets[2].data = dataTargetSpeed;
    this.chart.data.datasets[3].data = dataPower;

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
      for (const ds of this.chart.data.datasets) {
        ds.data = [];
      }
      this.chart.update();
    }
  }
}

window.ElevationProfileChart = ElevationProfileChart;
window.RideProfileChart = ElevationProfileChart;
