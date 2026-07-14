# AI Healthcare Learning Lab

`Learning`은 Linear의 `LRN-004`~`LRN-016`을 하나의 표준 C++17 실습으로 연결합니다.

- `NGramModel`: add-k smoothing과 perplexity (`LRN-004`)
- 벡터/행렬, softmax, embedding/cosine 유사도 (`LRN-005`, `LRN-006`)
- 작은 neural language model (`LRN-007`)
- scaled dot-product attention, positional encoding, transformer block (`LRN-008`, `LRN-009`)
- 대화형 로컬 LLM 클라이언트 인터페이스 (`LRN-010`)
- 의료 범위 제한과 금지 표현 정책 (`LRN-011`)
- Markdown/TXT loader와 overlap chunker (`LRN-012`)
- TF-IDF vector store와 top-k 검색 (`LRN-013`)
- 출처 기반 RAG 응답 (`LRN-014`)
- greedy/top-k/sample decoding과 안전 reward (`LRN-015`)
- safety → retrieval → generation → output guard 에이전트 루프 (`LRN-016`)

```powershell
cmake -S Learning -B Learning/build
cmake --build Learning/build --config Release
ctest --test-dir Learning/build -C Release --output-on-failure
```

CLI는 저장소 루트에서 빌드 디렉터리로 실행하면 `Assets/StreamingAssets/RagKnowledge` 문서를 근거로 사용합니다. 네트워크 LLM은 의도적으로 기본 구현에 포함하지 않았으며 `ILanguageModelClient` 구현을 주입해 교체할 수 있습니다.
