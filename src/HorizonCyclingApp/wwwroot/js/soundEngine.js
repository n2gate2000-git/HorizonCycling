// HorizonCycling Studio - Workout Sound & Audio Cue Engine
// Web Audio API によるゼロレイテンシ波形合成 & 音声ガイダンス

(function () {
  'use strict';

  class WorkoutSoundEngine {
    constructor() {
      this.ctx = null;
      this.enabled = true;
      this.countdownEnabled = true;
      this.transitionEnabled = true;
      this.ttsEnabled = false;
      this.volume = 0.8;

      // 追跡用内部ステート
      this.lastSegmentIndex = -1;
      this.lastCountdownSec = -1;
      this.lastWorkoutState = 'Idle';
      this.lastActiveMessage = null;

      this.loadSettings();
      this.setupAutoResume();
    }

    loadSettings() {
      try {
        const saved = localStorage.getItem('horizon_workout_sound_config');
        if (saved) {
          const cfg = JSON.parse(saved);
          if (typeof cfg.enabled === 'boolean') this.enabled = cfg.enabled;
          if (typeof cfg.countdownEnabled === 'boolean') this.countdownEnabled = cfg.countdownEnabled;
          if (typeof cfg.transitionEnabled === 'boolean') this.transitionEnabled = cfg.transitionEnabled;
          if (typeof cfg.ttsEnabled === 'boolean') this.ttsEnabled = cfg.ttsEnabled;
          if (typeof cfg.volume === 'number') this.volume = Math.max(0, Math.min(1, cfg.volume));
        }
      } catch (e) {
        console.warn('[SoundEngine] 設定の読み込みに失敗しました:', e);
      }
    }

    saveSettings() {
      try {
        const cfg = {
          enabled: this.enabled,
          countdownEnabled: this.countdownEnabled,
          transitionEnabled: this.transitionEnabled,
          ttsEnabled: this.ttsEnabled,
          volume: this.volume
        };
        localStorage.setItem('horizon_workout_sound_config', JSON.stringify(cfg));
      } catch (e) {
        console.warn('[SoundEngine] 設定の保存に失敗しました:', e);
      }
    }

    init() {
      if (!this.ctx) {
        const AudioContextClass = window.AudioContext || window.webkitAudioContext;
        if (AudioContextClass) {
          this.ctx = new AudioContextClass();
        }
      }
      if (this.ctx && this.ctx.state === 'suspended') {
        this.ctx.resume().catch(() => {});
      }
    }

    setupAutoResume() {
      const unlockAudio = () => {
        this.init();
        if (this.ctx && this.ctx.state === 'running') {
          window.removeEventListener('pointerdown', unlockAudio);
          window.removeEventListener('keydown', unlockAudio);
        }
      };
      window.addEventListener('pointerdown', unlockAudio, { passive: true });
      window.addEventListener('keydown', unlockAudio, { passive: true });
    }

    // -------------------------------------------------------------
    // サウンド合成メソッド群 (Web Audio API)
    // -------------------------------------------------------------

    // 3, 2, 1 カウントダウン予告音 (短音ピッ 880Hz / A5, 70ms)
    playCountdownTick(num) {
      if (!this.enabled || !this.countdownEnabled) return;
      this.init();
      if (!this.ctx) return;

      const now = this.ctx.currentTime;
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();

      osc.type = 'sine';
      // 1秒前はやや高めの 988Hz(B5) にして緊迫感を演出、3・2秒前は 880Hz(A5)
      const freq = (num === 1) ? 987.77 : 880.0;
      osc.frequency.setValueAtTime(freq, now);

      gain.gain.setValueAtTime(this.volume * 0.45, now);
      gain.gain.exponentialRampToValueAtTime(0.001, now + 0.07);

      osc.connect(gain);
      gain.connect(this.ctx.destination);

      osc.start(now);
      osc.stop(now + 0.07);
    }

    // Work開始 (高強度インターバル突入: 上昇アルペジオ「ピロリン！」)
    // C5 (523Hz) ➔ E5 (659Hz) ➔ C6 (1046Hz)
    playWorkStart() {
      if (!this.enabled || !this.transitionEnabled) return;
      this.init();
      if (!this.ctx) return;

      const baseTime = this.ctx.currentTime;
      const notes = [
        { freq: 523.25, time: 0.00, dur: 0.12, type: 'triangle' },
        { freq: 659.25, time: 0.07, dur: 0.14, type: 'triangle' },
        { freq: 1046.50, time: 0.14, dur: 0.35, type: 'triangle' }
      ];

      notes.forEach((note) => {
        const startTime = baseTime + note.time;
        const osc = this.ctx.createOscillator();
        const gain = this.ctx.createGain();

        osc.type = note.type;
        osc.frequency.setValueAtTime(note.freq, startTime);

        gain.gain.setValueAtTime(this.volume * 0.65, startTime);
        gain.gain.exponentialRampToValueAtTime(0.001, startTime + note.dur);

        osc.connect(gain);
        gain.connect(this.ctx.destination);

        osc.start(startTime);
        osc.stop(startTime + note.dur);
      });
    }

    // Rest開始 (回復・レスト突入: 下降ソフトトーン「ポロロン…」)
    // A5 (880Hz) ➔ E5 (659Hz) ➔ C5 (523Hz)
    playRestStart() {
      if (!this.enabled || !this.transitionEnabled) return;
      this.init();
      if (!this.ctx) return;

      const baseTime = this.ctx.currentTime;
      const notes = [
        { freq: 880.00, time: 0.00, dur: 0.16, type: 'sine' },
        { freq: 659.25, time: 0.09, dur: 0.18, type: 'sine' },
        { freq: 523.25, time: 0.18, dur: 0.40, type: 'sine' }
      ];

      notes.forEach((note) => {
        const startTime = baseTime + note.time;
        const osc = this.ctx.createOscillator();
        const gain = this.ctx.createGain();

        osc.type = note.type;
        osc.frequency.setValueAtTime(note.freq, startTime);

        gain.gain.setValueAtTime(this.volume * 0.55, startTime);
        gain.gain.exponentialRampToValueAtTime(0.001, startTime + note.dur);

        osc.connect(gain);
        gain.connect(this.ctx.destination);

        osc.start(startTime);
        osc.stop(startTime + note.dur);
      });
    }

    // ランプ・一定強度等の中立的セグメント遷移音 (ベルトーン G5: 784Hz)
    playTransition() {
      if (!this.enabled || !this.transitionEnabled) return;
      this.init();
      if (!this.ctx) return;

      const now = this.ctx.currentTime;
      const osc = this.ctx.createOscillator();
      const gain = this.ctx.createGain();

      osc.type = 'sine';
      osc.frequency.setValueAtTime(783.99, now); // G5

      gain.gain.setValueAtTime(this.volume * 0.55, now);
      gain.gain.exponentialRampToValueAtTime(0.001, now + 0.28);

      osc.connect(gain);
      gain.connect(this.ctx.destination);

      osc.start(now);
      osc.stop(now + 0.28);
    }

    // ワークアウト完走ファンファーレ (華やかなメジャー和音メロディ)
    playFinished() {
      if (!this.enabled || !this.transitionEnabled) return;
      this.init();
      if (!this.ctx) return;

      const baseTime = this.ctx.currentTime;
      // 和音シーケンス: Cメジャー ➔ Fメジャー ➔ Gメジャー ➔ Cオクターブ
      const chords = [
        { time: 0.00, dur: 0.16, freqs: [523.25, 659.25] },          // C5, E5
        { time: 0.16, dur: 0.16, freqs: [587.33, 698.46] },          // D5, F5
        { time: 0.32, dur: 0.20, freqs: [659.25, 783.99] },          // E5, G5
        { time: 0.52, dur: 0.70, freqs: [523.25, 659.25, 1046.50] }  // C5, E5, C6 (響き)
      ];

      chords.forEach((chord) => {
        chord.freqs.forEach((freq) => {
          const startTime = baseTime + chord.time;
          const osc = this.ctx.createOscillator();
          const gain = this.ctx.createGain();

          osc.type = 'triangle';
          osc.frequency.setValueAtTime(freq, startTime);

          gain.gain.setValueAtTime((this.volume * 0.5) / chord.freqs.length, startTime);
          gain.gain.exponentialRampToValueAtTime(0.001, startTime + chord.dur);

          osc.connect(gain);
          gain.connect(this.ctx.destination);

          osc.start(startTime);
          osc.stop(startTime + chord.dur);
        });
      });
    }

    // 音声ガイダンス読み上げ (Web Speech API)
    speak(text, force = false) {
      if ((!this.enabled || !this.ttsEnabled) && !force) return;
      if (!window.speechSynthesis) return;
      try {
        window.speechSynthesis.cancel(); // 連続発話時のキューをクリア
        const isEn = window.i18n && window.i18n.getLanguage() === 'en';
        const uttr = new SpeechSynthesisUtterance(text);
        uttr.lang = isEn ? 'en-US' : 'ja-JP';
        uttr.volume = this.volume;
        uttr.rate = 1.1; // やや早口でテンポ良く
        uttr.pitch = 1.05;

        // 言語に応じた音声を優先探索
        const voices = window.speechSynthesis.getVoices();
        const targetVoice = isEn
          ? voices.find(v => v.lang && v.lang.startsWith('en'))
          : voices.find(v => v.lang && (v.lang.startsWith('ja') || v.lang === 'ja_JP'));
        if (targetVoice) uttr.voice = targetVoice;

        window.speechSynthesis.speak(uttr);
      } catch (e) {
        console.warn('[SoundEngine] TTS発声エラー:', e);
      }
    }

    // -------------------------------------------------------------
    // 状態更新ハンドラ (30Hz/1Hzのワークアウト状態スナップショットを監視)
    // -------------------------------------------------------------
    update(workout) {
      if (!workout || !workout.IsActive) {
        // ワークアウト終了/停止時
        if (this.lastWorkoutState === 'Running' && workout && workout.State === 'Finished') {
          this.playFinished();
          if (this.ttsEnabled) {
            const isEn = window.i18n && window.i18n.getLanguage() === 'en';
            this.speak(isEn ? 'Workout completed! Great effort!' : 'ワークアウト完了です。素晴らしい走りでした！');
          }
        }
        this.lastSegmentIndex = -1;
        this.lastCountdownSec = -1;
        this.lastActiveMessage = null;
        this.lastWorkoutState = workout ? workout.State : 'Idle';
        return;
      }

      // 1. カウントダウン予告音の判定 (残り 3, 2, 1 秒時)
      if (workout.State === 'Running' && workout.CurrentSegmentIndex + 1 < workout.TotalSegmentsCount) {
        const rem = workout.SegmentRemainingSec;
        if (rem >= 1 && rem <= 3) {
          if (this.lastCountdownSec !== rem) {
            this.lastCountdownSec = rem;
            this.playCountdownTick(rem);
          }
        } else {
          this.lastCountdownSec = -1;
        }
      }

      // 2. セグメント切り替え瞬間の判定
      const curIdx = workout.CurrentSegmentIndex;
      if (curIdx !== this.lastSegmentIndex) {
        const isInitialStart = (this.lastSegmentIndex === -1);
        const prevIdx = this.lastSegmentIndex;
        this.lastSegmentIndex = curIdx;
        this.lastCountdownSec = -1;

        // ワークアウト開始直後、またはステップ遷移時
        if (workout.State === 'Running') {
          const segs = workout.Segments || [];
          const curSeg = segs[curIdx];
          const prevSeg = (prevIdx >= 0 && prevIdx < segs.length) ? segs[prevIdx] : null;

          const curType = (workout.CurrentSegmentType || '').toLowerCase();
          const targetWatts = workout.TargetWatts || 0;

          // Work (高強度) か Rest (回復) かの判定
          let isWork = false;
          let isRest = false;

          if (curType.includes('on') || curType.includes('interval')) {
            isWork = true;
          } else if (curType.includes('off') || curType.includes('rest') || curType.includes('cooldown')) {
            isRest = true;
          } else if (prevSeg && curSeg) {
            if (curSeg.TargetWattsLow >= prevSeg.TargetWattsLow * 1.05) {
              isWork = true;
            } else if (curSeg.TargetWattsLow <= prevSeg.TargetWattsLow * 0.95) {
              isRest = true;
            }
          } else if (isInitialStart) {
            // 開始時: ウォームアップでなければWork
            isWork = !curType.includes('warmup');
          }

          if (isWork) {
            this.playWorkStart();
            if (this.ttsEnabled) {
              this.speak(`インターバル開始、目標${targetWatts}ワット`);
            }
          } else if (isRest) {
            this.playRestStart();
            if (this.ttsEnabled) {
              this.speak(`レスト、目標${targetWatts}ワット`);
            }
          } else {
            this.playTransition();
            if (this.ttsEnabled) {
              this.speak(`目標${targetWatts}ワット`);
            }
          }
        }
      }

      // 3. コーチングテキストメッセージのTTS読み上げ
      if (this.ttsEnabled && workout.ActiveMessage && workout.ActiveMessage !== this.lastActiveMessage) {
        this.lastActiveMessage = workout.ActiveMessage;
        this.speak(workout.ActiveMessage);
      } else if (!workout.ActiveMessage) {
        this.lastActiveMessage = null;
      }

      this.lastWorkoutState = workout.State;
    }
  }

  // グローバルシングルトンとして登録
  window.workoutSoundEngine = new WorkoutSoundEngine();

})();
