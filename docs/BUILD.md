# HorizonCycling ビルド手順書 (Build Guide)

本ドキュメントでは、`HorizonCycling` (`HorizonCyclingBridge`) の開発環境構築、ビルド、およびリリース用パッケージのパブリッシュ手順について解説します。

---

## 1. 動作・開発前提環境 (Prerequisites)

本アプリケーションは .NET 8.0 を基盤とし、Windows BLE (Bluetooth Low Energy) API および vJoy 仮想ドライバーと通信するため、以下の環境が必要です。

- **OS**: Windows 10 (バージョン 2004 / ビルド 19041 以降) または Windows 11
- **.NET SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 以降 (Version 8.0.x 以上、.NET 9/10/11 などの上位 SDK も利用可能)
- **Windows SDK**: Windows 10 SDK (10.0.19041.0) 以降
- **開発ツール (推奨のいずれか)**:
  - **Visual Studio 2022** (v17.8 以降、「.NET デスクトップ開発」ワークロード導入済み)
  - **Visual Studio Code** + C# Extension / C# Dev Kit
  - **.NET CLI** (コマンドライン環境)

---

## 2. 開発環境の準備

### 2.1 リポジトリのクローン
```bash
git clone <リポジトリURL>
cd HorizonCycling
```

### 2.2 プロジェクト構成
主要なソースコードおよびプロジェクトファイルは `src/` ディレクトリ配下に配置されています。

- **プロジェクトファイル**: `src/HorizonCyclingBridge/HorizonCyclingBridge.csproj`
- **ターゲットフレームワーク**: `net8.0-windows10.0.19041.0`

---

## 3. ビルド手順

### 3.1 .NET CLI を使用したビルド

プロジェクトルート (`d:\develop\HorizonCycling`) で以下のコマンドを実行します。

#### デバッグビルド (Debug Build)
```powershell
dotnet build src/HorizonCyclingBridge/HorizonCyclingBridge.csproj -c Debug
```

#### リリースビルド (Release Build)
```powershell
dotnet build src/HorizonCyclingBridge/HorizonCyclingBridge.csproj -c Release
```

ビルド成果物は `src/HorizonCyclingBridge/bin/Release/net8.0-windows10.0.19041.0/` に出力されます。

---

### 3.2 Visual Studio 2022 を使用したビルド

1. Visual Studio 2022 を起動し、[ファイル] -> [開く] -> [プロジェクト/ソリューション] から `src/HorizonCyclingBridge/HorizonCyclingBridge.csproj` を選択します。
2. ツールバーのソリューション構成を `Release` (または `Debug`) に設定します。
3. メニューの [ビルド] -> [ソリューションのビルド] (または `Ctrl + Shift + B`) を実行します。

---

## 4. パブリッシュ (.NETランタイム同梱・単一EXEの生成)

本プロジェクトは `.csproj` 内で **自己完結型 (Self-contained)** および **単一ファイル出力 (PublishSingleFile)** が有効化されています。.NET ランタイムが未インストールの Windows 環境でも、配布用 EXE 単体で動作します。

以下のコマンドでリリース用パブリッシュを実行します。

```powershell
dotnet publish src/HorizonCyclingBridge/HorizonCyclingBridge.csproj -c Release -o release/HorizonCyclingBridge
```

これにより、`release/HorizonCyclingBridge` フォルダ内に .NET ランタイムを全て含んだ単一の実行ファイル `HorizonCyclingBridge.exe` が生成されます。

> [!NOTE]
> 従来のフレームワーク依存 (ランタイム非同梱) の軽量構成でパブリッシュしたい場合は `--self-contained false -p:PublishSingleFile=false` を指定してください。

---

## 5. ビルド後の必須手順 (vJoyInterface.dll の配置)

アプリケーションを起動・実行するためには、64bit版の **`vJoyInterface.dll`** が実行ファイル (`HorizonCyclingBridge.exe`) と**同じディレクトリ**に配置されている必要があります。

1. **vJoy がインストールされている環境**:
   `C:\Program Files\vJoy\x64\vJoyInterface.dll` からコピーします。
2. **手動配置**:
   パブリッシュ出力先ディレクトリ (例: `release/HorizonCyclingBridge/`) またはビルド出力先ディレクトリ (`bin/Release/.../`) の直下に `vJoyInterface.dll` を配置してください。

> [!IMPORTANT]
> `vJoyInterface.dll` が存在しない場合、アプリ起動時に Native DLL 読み込みエラーが発生するか、vJoy との通信に失敗します。

---

## 6. 動作確認

ビルドおよび DLL の配置が完了したら、以下のようにコマンドプロンプトや PowerShell から実行して動作確認を行います。

```powershell
# パブリッシュ出力先から実行する場合
cd release/HorizonCyclingBridge
.\HorizonCyclingBridge.exe
```

---

## 7. トラブルシューティング

### `No .NET SDKs were found` エラーが発生する場合
`dotnet build` または `dotnet publish` 実行時に下記のエラーが発生する場合、PC に .NET 8.0 SDK (開発環境) がインストールされていません。

```text
The command could not be loaded, possibly because:
  * You intended to execute a .NET application:
      The application 'build' does not exist.
  * You intended to execute a .NET SDK command:
      No .NET SDKs were found.
```

#### 対処法
1. **.NET 8.0 SDK の手動インストール**
   - [.NET 8.0 SDK 公式ダウンロードページ](https://dotnet.microsoft.com/download/dotnet/8.0) から **Windows x64 用 SDK** をダウンロードしてインストールします。（※Runtime ではなく **SDK** が必要です）
2. **Visual Studio Installer を使用する場合**
   - Visual Studio Installer を開き、Visual Studio 2022 の「変更」から **「.NET デスクトップ開発」** ワークロードにチェックを入れてインストールします。
3. **確認**
   - ターミナルを再起動し、`dotnet --list-sdks` を実行してバージョン（例: `8.0.xxx`）が表示されることを確認します。

