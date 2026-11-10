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

## 初期サーバー管理者
サーバー管理者は全ルームを**閲覧**できる唯一のアカウントです（編集はできません。ADR-003）。
管理者が1人もいない時に限り、次の設定から初期管理者を作成します。パスワードはコミットせず、環境変数かユーザーシークレットで渡してください。

```bash
cd src/Fortuna.Server
dotnet user-secrets set "Fortuna:BootstrapAdmin:Email" "admin@example.com"
dotnet user-secrets set "Fortuna:BootstrapAdmin:Password" "<10文字以上、大小英字・数字・記号を含む>"
```

## API の概要
| 用途 | エンドポイント |
|---|---|
| 登録 / ログイン | `POST /api/auth/register`、`POST /api/auth/login?useCookies=true` |
| 自分の情報 | `GET` / `PUT /api/me` |
| ルーム | `POST` / `GET /api/rooms`、`POST /api/rooms/join`、`GET /api/rooms/{id}/members`、`/leave`、`/kick`、`/transfer` |
| リアルタイム | SignalR `/hubs/room`（`EnterRoom`、`AddObject`、`UpdateContent`、`SetAudience`、`RemoveObject`。受信は `ViewEvent`） |

## 現状の制限
- ルームの状態は**メモリのみ**で、サーバーを再起動すると消えます（永続化は未実装）。
- DB スキーマは `EnsureCreated` で作成しています（EF Migrations は未導入）。
- Google ログイン、ルーム削除、素材のアップロードは未実装です。

## ライセンス
MIT
