#include "ahc_learning.hpp"

#include <cmath>
#include <cstdlib>
#include <iostream>
#include <stdexcept>

namespace
{
void require(bool condition, const char* message)
{
    if (!condition)
    {
        std::cerr << "FAILED: " << message << "\n";
        std::exit(1);
    }
}
}

int main()
{
    using namespace ahc::learning;
    require(std::abs(dot({1, 2}, {3, 4}) - 11.0) < 1e-9, "dot product");
    const auto probabilities = softmax({1, 2, 3});
    require(probabilities.size() == 3 && std::abs(probabilities[0] + probabilities[1] + probabilities[2] - 1.0) < 1e-9, "softmax");

    NGramModel ngram(2, 1.0);
    ngram.train({"squat", "slow", "squat", "safe"});
    require(std::isfinite(ngram.perplexity({"squat", "slow", "squat"})), "perplexity");

    EmbeddingTable embeddings(4);
    embeddings.get("squat"); embeddings.get("lunge");
    require(embeddings.nearest("squat", 1).size() == 1, "embedding search");

    TinyNeuralLanguageModel neural({"safe", "slow", "stop"}, 4);
    const double before = neural.trainStep("safe", "slow", 0.1);
    const double after = neural.trainStep("safe", "slow", 0.1);
    require(after <= before + 1e-6, "neural language model training");

    const Matrix attention = scaledDotProductAttention({{1, 0}}, {{1, 0}, {0, 1}}, {{2, 0}, {0, 2}});
    require(attention.size() == 1 && attention.front().size() == 2, "attention");
    require(transformerBlock({{0.1, 0.2}, {0.3, 0.4}}).size() == 2, "transformer block");

    HealthcareSafetyPolicy policy;
    require(!policy.evaluate("I have chest pain").allowed, "emergency safety gate");
    require(!policy.evaluate("diagnose my knee").allowed, "medical scope gate");

    DocumentChunker chunker(8, 2);
    TfidfVectorStore store;
    const auto chunks = chunker.chunk("squat.md", "keep both knees aligned with the toes and move slowly");
    store.build(chunks);
    const auto searchResults = store.search("knees aligned", 2);
    require(!searchResults.empty(), "tf-idf retrieval");

    Decoder decoder;
    require(decoder.choose({0.1, 0.9}, DecodingStrategy::Greedy) == 1, "greedy decoding");
    require(decoder.reward("A safe cited coaching response.", policy, true) > 0.9, "reward score");

    LocalTemplateClient client;
    HealthcareAgent agent(store, policy, client);
    const auto answer = agent.run("How should both knees stay aligned during a squat?");
    require(answer.success && !answer.citations.empty() && answer.trace.size() == 4, "RAG agent loop");

    std::cout << "All AI Healthcare learning tests passed.\n";
    return 0;
}
