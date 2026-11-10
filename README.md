# FortunaVTT

秘匿を第一に考えた、ブラウザで動くサーバー権威型のTRPG向けバーチャルテーブルトップ。
「どどんとふ」の使い勝手と秘匿性を、現代の技術で作り直すプロジェクトです。

## 特徴（目標）
- **秘匿が機能する**: サーバーが「誰に見せてよいか」を判定し、見えないデータはクライアントに送らない。
- **インストール不要**: ブラウザだけで遊べる。
- **リアルタイム**: ポーリングではなく SignalR (WebSocket) による差分配信。
- **BCDice 対応**: C# 移植版 BCDice を利用。

## 構成
| ディレクトリ | 内容 |
|---|---|
| `src/Fortuna.Server` | ASP.NET Core (.NET 10) + SignalR |
| `src/Fortuna.Domain` | ドメイン層（Room / Token / 可視性判定など） |
| `tests/Fortuna.Tests` | xUnit テスト |
| `client` | TypeScript + Vite のWebクライアント |
| `docs/adr` | 設計判断の記録 |

## 開発
```bash
dotnet build FortunaVTT.slnx
dotnet test FortunaVTT.slnx
dotnet run --project src/Fortuna.Server

cd client && npm install && npm run dev
```

## ライセンス
MIT
