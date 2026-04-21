"""
ACS（Azure Communication Services）接続テスト
- ACSに接続できるか確認
- ユーザーIDを作成してボットの身元を確認
"""
import os
import sys


def load_env_user(filepath):
    """env.local.user から環境変数を読み込む"""
    env = {}
    try:
        with open(filepath, "r", encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                if "=" not in line:
                    continue
                key, _, value = line.partition("=")
                key = key.strip()
                # SECRET_ プレフィックスを除去
                if key.startswith("SECRET_"):
                    key = key[7:]
                env[key] = value.strip().strip("'\"")
    except FileNotFoundError:
        print(f"❌ ファイルが見つかりません: {filepath}")
    return env


# .env.local.user から接続文字列を読み込む
env_path = os.path.join(os.path.dirname(__file__), "..", "env", ".env.local.user")
env = load_env_user(env_path)

# ACS接続文字列は "endpoint=...;accesskey=..." の形式
# SECRET_ を除去した後のキー名 "ACS_CONNECTION_STRING" を探すが、
# .env.local.user での値が "endpoint=..." で始まるため結合する
acs_connection_string = None
for key, value in env.items():
    if "ACS_CONNECTION_STRING" in key:
        acs_connection_string = f"endpoint={value}" if not value.startswith("endpoint=") else value
        break

if not acs_connection_string:
    print("❌ ACS_CONNECTION_STRING が .env.local.user に見つかりません")
    sys.exit(1)

print(f"接続先: {acs_connection_string.split(';')[0]}")


def test_acs_connection():
    from azure.communication.identity import CommunicationIdentityClient

    print("\n--- ACS 接続テスト ---")
    client = CommunicationIdentityClient.from_connection_string(acs_connection_string)

    # テスト用ユーザーを作成（ボットがTeams会議に参加するための身元）
    print("テストユーザーを作成中...")
    user = client.create_user()
    user_id = user.properties["id"]
    print(f"✅ ACS接続成功！")
    print(f"   ボット用ユーザーID: {user_id}")

    # テスト後にクリーンアップ
    client.delete_user(user)
    print("✅ テストユーザー削除完了（クリーンアップ済み）")
    print("\n🎉 ACSは正常に動作しています！")


if __name__ == "__main__":
    test_acs_connection()
