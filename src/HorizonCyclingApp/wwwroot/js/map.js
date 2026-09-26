/**
 * HorizonCycling Studio - Live Map Tracker (Leaflet.js)
 * High-definition Full 8192x8192 Map Integration
 */

class LiveMapTracker {
  constructor(containerId) {
    this.containerId = containerId;
    this.map = null;
    this.carMarker = null;
    this.trackLine = null;
    this.trackSegments = [[]];
    this.followCar = true;
    this.poiLayerGroup = null;
    this.pinsVisible = true;

    // 8192 x 8192 座標系設定
    this.mapSize = 8192;
  }

  init() {
    // Leaflet CRS.Simple (平面直交座標系)
    this.map = L.map(this.containerId, {
      crs: L.CRS.Simple,
      minZoom: -3,
      maxZoom: 3,
      zoomControl: false,
      attributionControl: false
    });

    // ズームコントロールを右下に配置
    L.control.zoom({ position: 'bottomright' }).addTo(this.map);

    // フルマップ画像の境界 [ [ymin, xmin], [ymax, xmax] ]
    // Leaflet CRS.Simple では y = 0..8192 (南から北), x = 0..8192 (西から東)
    const fullBounds = [[0, 0], [this.mapSize, this.mapSize]];

    // 高精細フルマップ画像 (fh6_japan_map_full_8192x8192.jpg) のオーバーレイ
    // 海背景 (#2d4555) を含む全域がどのズームレベルでも滑らかに描画されます
    const fullMapUrl = 'https://maps.local/fh6_japan_map_full_8192x8192.jpg';
    const imageOverlay = L.imageOverlay(fullMapUrl, fullBounds, {
      errorOverlayUrl: 'https://maps.local/fh6_japan_map_cropped_4608x5632.jpg'
    });
    imageOverlay.addTo(this.map);

    // 全体が表示されるように初期フィット
    this.map.fitBounds(fullBounds);

    // 最大パン境界の制限（地図外へ迷子にならないようにする）
    this.map.setMaxBounds([[-1000, -1000], [this.mapSize + 1000, this.mapSize + 1000]]);

    // 走行軌跡 Polyline (ネオンシアン #06b6d4)
    this.trackLine = L.polyline([], {
      color: '#06b6d4',
      weight: 4,
      opacity: 0.9,
      lineJoin: 'round',
      smoothFactor: 1.0
    }).addTo(this.map);

    // 自車アイコン (ネオングリーンの矢印)
    const carIcon = L.divIcon({
      className: 'car-marker-container',
      html: `
        <div id="carArrow" class="car-arrow" style="transform: rotate(0deg); transition: transform 0.1s linear;">
          <svg width="32" height="32" viewBox="0 0 24 24" fill="none" style="filter: drop-shadow(0 0 8px #10b981);">
            <path d="M12 2L2 22L12 18L22 22L12 2Z" fill="#10b981" stroke="#ffffff" stroke-width="2"/>
          </svg>
        </div>
      `,
      iconSize: [32, 32],
      iconAnchor: [16, 16]
    });

    const initialCenter = this.pixelToLatLng(this.mapSize / 2, this.mapSize / 2);
    this.carMarker = L.marker(initialCenter, { icon: carIcon }).addTo(this.map);

    // POIレイヤーグループ
    this.poiLayerGroup = L.layerGroup().addTo(this.map);

    // キャリブレーション時のマップクリック処理
    this.isCalibrating = false;
    this.onCalibrationClick = null;

    this.map.on('click', (e) => {
      if (this.isCalibrating && this.onCalibrationClick) {
        const px = e.latlng.lng;
        const py = this.mapSize - e.latlng.lat;
        this.onCalibrationClick(px, py);
      }
    });

    // ユーザーが手動でドラッグした場合のハンドリング
    this.map.on('dragstart', () => {
      // ユーザーの自由な閲覧を妨げない
    });
  }

  // 8192ピクセル座標 (左上 0,0 〜 右下 8192,8192) を Leaflet CRS.Simple (左下 0,0 〜 右上 8192,8192) に変換
  pixelToLatLng(px, py) {
    const lat = this.mapSize - py;
    const lng = px;
    return L.latLng(lat, lng);
  }

  // Leaflet CRS.Simple の latLng を 8192ピクセル座標 (px, py) に逆変換
  latLngToPixel(latLng) {
    return {
      px: latLng.lng,
      py: this.mapSize - latLng.lat
    };
  }

  setCalibrationMode(enabled, callback) {
    this.isCalibrating = enabled;
    this.onCalibrationClick = callback;
    if (this.map && this.map.getContainer()) {
      this.map.getContainer().style.cursor = enabled ? 'crosshair' : '';
    }
  }

  // 車両の位置と向きを更新
  updateCar(pixelX, pixelY, yawRadians, worldX, worldZ, isPositionValid = true, isTeleport = false) {
    if (!this.map) return;

    // メニュー中や0,0,0へのジャンプガード
    const isZeroWorld = (typeof worldX === 'number' && typeof worldZ === 'number' && Math.abs(worldX) < 0.0001 && Math.abs(worldZ) < 0.0001);
    const isValid = (isPositionValid !== false) && !isZeroWorld;

    const latLng = this.pixelToLatLng(pixelX, pixelY);

    // マーカー位置移動
    this.carMarker.setLatLng(latLng);

    // 矢印の回転 (Yaw はラジアン。ForzaのYawを度数法に変換)
    const deg = (yawRadians * (180.0 / Math.PI));
    const arrowElem = document.getElementById('carArrow');
    if (arrowElem) {
      arrowElem.style.transform = `rotate(${deg}deg)`;
    }

    // 軌跡の追加（メニュー中や0,0,0の時は軌跡を描かない）
    if (isValid) {
      let currentSeg = this.trackSegments[this.trackSegments.length - 1];

      if (currentSeg.length === 0) {
        currentSeg.push(latLng);
        this.trackLine.setLatLngs(this.trackSegments);
      } else {
        const last = currentSeg[currentSeg.length - 1];
        const dist = Math.hypot(latLng.lat - last.lat, latLng.lng - last.lng);

        // ファストトラベル・ワープ判定（フラグまたはピクセル距離急変）
        const isJump = (isTeleport === true) || (dist >= 45.0);

        if (isJump) {
          // 移動前の軌跡はそのまま残し、移動前と移動後の間に直線を引かず新しいセグメントを開始
          this.trackSegments.push([latLng]);
          this.trackLine.setLatLngs(this.trackSegments);
        } else if (dist > 5.0) {
          // 通常の走行移動: 現在のセグメントに追加
          currentSeg.push(latLng);
          this.trackLine.setLatLngs(this.trackSegments);

          // メモリ・描画負荷対策（最大3000点）
          let totalPoints = 0;
          for (let i = 0; i < this.trackSegments.length; i++) {
            totalPoints += this.trackSegments[i].length;
          }
          if (totalPoints > 3000) {
            for (let i = 0; i < this.trackSegments.length; i++) {
              if (this.trackSegments[i].length > 0) {
                this.trackSegments[i].shift();
                if (this.trackSegments[i].length === 0 && this.trackSegments.length > 1) {
                  this.trackSegments.splice(i, 1);
                }
                break;
              }
            }
          }
        }
      }
    }

    // 自車追従が有効な場合、滑らかにパン
    if (this.followCar) {
      this.map.panTo(latLng, { animate: true, duration: 0.15 });
    }
  }

  // POIピン一覧を配置
  setPins(pins) {
    if (!this.poiLayerGroup) return;
    this.poiLayerGroup.clearLayers();

    pins.forEach(pin => {
      if (pin.PixelX && pin.PixelY) {
        const latLng = this.pixelToLatLng(pin.PixelX, pin.PixelY);
        const marker = L.circleMarker(latLng, {
          radius: 6,
          color: '#ffffff',
          weight: 2,
          fillColor: '#f59e0b',
          fillOpacity: 0.9
        }).bindPopup(`
          <div style="font-family: sans-serif; color: #1e293b;">
            <b style="font-size: 13px; color: #0f172a;">${pin.Name}</b><br>
            <span style="font-size: 11px; color: #475569;">${pin.Description}</span>
          </div>
        `);

        this.poiLayerGroup.addLayer(marker);
      }
    });

    if (!this.pinsVisible && this.map.hasLayer(this.poiLayerGroup)) {
      this.map.removeLayer(this.poiLayerGroup);
    }
  }

  togglePins(visible) {
    this.pinsVisible = typeof visible === 'boolean' ? visible : !this.pinsVisible;
    if (this.poiLayerGroup && this.map) {
      if (this.pinsVisible) {
        if (!this.map.hasLayer(this.poiLayerGroup)) {
          this.map.addLayer(this.poiLayerGroup);
        }
      } else {
        if (this.map.hasLayer(this.poiLayerGroup)) {
          this.map.removeLayer(this.poiLayerGroup);
        }
      }
    }
    return this.pinsVisible;
  }

  clearTrack() {
    this.trackSegments = [[]];
    if (this.trackLine) {
      this.trackLine.setLatLngs([]);
    }
  }

  resetView() {
    if (!this.map) return;
    this.map.fitBounds([[0, 0], [this.mapSize, this.mapSize]]);
  }

  setFollowCar(enabled) {
    this.followCar = enabled;
  }
}

window.LiveMapTracker = LiveMapTracker;
