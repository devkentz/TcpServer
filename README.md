# Network Server Solution

이 프로젝트는 .NET Core 기반의 서버 및 클라이언트 프레임워크입니다.

## 🛠 기술 스택 (Tech Stack)

- **Framework**: .NET 8 / 9
- **Serialization**: Google Protobuf
- **Database / Cache**: Redis (StackExchange.Redis)
- **Logging**: Serilog
- **Utilities**: NetMQ

## 📂 프로젝트 구조 (Project Structure)

| 경로 | 설명 |
| --- | --- |
| `NetworkServer.TcpServer/` | TCP 서버의 핵심 로직 (Actor, Config, Core 등)이 포함되어 있습니다. |
| `NetworkClient/` | 서버와 통신하기 위한 클라이언트 라이브러리입니다. |
| `Protocol/` | 통신 프로토콜 정의 및 메시지 처리를 담당합니다. |
| `Sample/` | 서버 및 클라이언트 사용 예제를 포함하는 샘플 프로젝트입니다. |
| `NetworkServer.ProtoGenerator/` | 프로토콜 버퍼 파일로부터 코드를 생성하는 도구입니다. |

## 🏁 시작하기 (Getting Started)

### 필수 조건 (Prerequisites)

- .NET SDK 8.0 이상
- Redis
 