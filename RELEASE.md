# Release Automation

GameChatTranslator 릴리즈는 GitHub Actions의 `Release Build` workflow로 자동 빌드할 수 있습니다.

## 릴리즈 순서

1. `GameChatTranslator/GameChatTranslator.csproj`의 버전을 새 버전으로 변경합니다.

   예:

   ```xml
   <Version>1.0.8-alpha</Version>
   <AssemblyVersion>1.0.8.0</AssemblyVersion>
   <FileVersion>1.0.8.0</FileVersion>
   ```

2. `README.md`의 다운로드 링크와 최신 업데이트 내역을 새 버전으로 변경합니다.

3. `GameChatTranslator/readme.txt`의 버전 표기를 새 버전으로 변경합니다.

4. 변경사항을 `master`에 머지합니다.

5. `master` 최신 커밋에 릴리즈 태그를 생성하고 push합니다.

   ```bash
   git checkout master
   git pull --ff-only origin master
   git tag v.1.0.8-alpha
   git push origin v.1.0.8-alpha
   ```

6. GitHub Actions의 `Release Build` workflow가 자동으로 실행됩니다.

## 자동 처리 내용

workflow는 태그 push를 감지하면 아래 작업을 수행합니다.

- Windows `windows-latest` 환경에서 .NET 8 설치
- `GameChatTranslator.sln` Release 빌드
- `win-x64` self-contained single-file publish
- `GameChatTranslator_v1.0.8-alpha.zip` 생성
- `sha256.txt` 체크섬 생성
- GitHub Release 생성 또는 기존 Release 갱신
- zip과 checksum asset 업로드
- `make_latest=true`로 최신 릴리즈 지정

## 태그와 버전 검증

workflow는 태그와 `.csproj`의 `<Version>` 값이 일치하는지 검사합니다.

예:

```text
<Version>1.0.8-alpha</Version>
tag v.1.0.8-alpha
```

두 값이 다르면 릴리즈를 중단합니다.

## 실패 후 재실행

태그 push 후 workflow가 실패했다면 GitHub Actions 화면에서 `Release Build`를 수동 실행할 수 있습니다.

수동 실행 시 `tag` 입력칸에 기존 태그를 입력합니다.

```text
v.1.0.8-alpha
```

workflow는 같은 태그의 Release가 이미 있으면 내용을 갱신하고 asset을 `--clobber`로 다시 업로드합니다.
