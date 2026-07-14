#pragma once

#include <cstddef>
#include <functional>
#include <map>
#include <random>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

namespace ahc::learning
{
using Vector = std::vector<double>;
using Matrix = std::vector<Vector>;

double dot(const Vector& left, const Vector& right);
double cosineSimilarity(const Vector& left, const Vector& right);
Vector softmax(const Vector& values, double temperature = 1.0);
Matrix matrixMultiply(const Matrix& left, const Matrix& right);

class NGramModel
{
public:
    explicit NGramModel(std::size_t order = 2, double smoothing = 1.0);
    void train(const std::vector<std::string>& tokens);
    double probability(const std::vector<std::string>& context, const std::string& token) const;
    double perplexity(const std::vector<std::string>& tokens) const;

private:
    std::string key(const std::vector<std::string>& tokens, std::size_t begin, std::size_t count) const;
    std::size_t order_;
    double smoothing_;
    std::unordered_map<std::string, std::size_t> ngramCounts_;
    std::unordered_map<std::string, std::size_t> contextCounts_;
    std::unordered_map<std::string, std::size_t> vocabulary_;
};

class EmbeddingTable
{
public:
    explicit EmbeddingTable(std::size_t dimensions = 8, unsigned seed = 7);
    const Vector& get(const std::string& token);
    std::vector<std::pair<std::string, double>> nearest(const std::string& token, std::size_t limit);

private:
    std::size_t dimensions_;
    std::mt19937 generator_;
    std::map<std::string, Vector> values_;
};

class TinyNeuralLanguageModel
{
public:
    TinyNeuralLanguageModel(std::vector<std::string> vocabulary, std::size_t dimensions = 8, unsigned seed = 11);
    Vector probabilities(const std::string& previousToken);
    double trainStep(const std::string& previousToken, const std::string& expectedToken, double learningRate);
    const std::vector<std::string>& vocabulary() const;

private:
    std::size_t indexOf(const std::string& token) const;
    std::vector<std::string> vocabulary_;
    EmbeddingTable embeddings_;
    Matrix outputWeights_;
};

Matrix scaledDotProductAttention(const Matrix& queries, const Matrix& keys, const Matrix& values);
Matrix positionalEncoding(std::size_t positions, std::size_t dimensions);
Matrix transformerBlock(const Matrix& input);

struct SafetyDecision
{
    bool allowed = true;
    bool shouldStopExercise = false;
    std::string code;
    std::string message;
};

class HealthcareSafetyPolicy
{
public:
    SafetyDecision evaluate(const std::string& input) const;
    bool validateOutput(const std::string& output) const;
};

struct DocumentChunk
{
    std::string id;
    std::string source;
    std::string text;
};

class DocumentChunker
{
public:
    explicit DocumentChunker(std::size_t maxWords = 80, std::size_t overlapWords = 12);
    std::vector<DocumentChunk> chunk(const std::string& source, const std::string& text) const;
    std::vector<DocumentChunk> loadDirectory(const std::string& directory) const;

private:
    std::size_t maxWords_;
    std::size_t overlapWords_;
};

struct SearchResult
{
    DocumentChunk chunk;
    double score = 0.0;
};

class TfidfVectorStore
{
public:
    void build(std::vector<DocumentChunk> chunks);
    std::vector<SearchResult> search(const std::string& query, std::size_t limit) const;

private:
    Vector vectorize(const std::string& text) const;
    std::vector<DocumentChunk> chunks_;
    std::map<std::string, std::size_t> vocabulary_;
    Vector inverseDocumentFrequency_;
    Matrix vectors_;
};

enum class DecodingStrategy { Greedy, TopK, Sample };

class Decoder
{
public:
    explicit Decoder(unsigned seed = 17);
    std::size_t choose(const Vector& logits, DecodingStrategy strategy, std::size_t topK = 3, double temperature = 0.7);
    double reward(const std::string& text, const HealthcareSafetyPolicy& policy, bool hasCitation) const;

private:
    std::mt19937 generator_;
};

class ILanguageModelClient
{
public:
    virtual ~ILanguageModelClient() = default;
    virtual std::string complete(const std::string& prompt, double temperature) = 0;
};

class LocalTemplateClient final : public ILanguageModelClient
{
public:
    std::string complete(const std::string& prompt, double temperature) override;
};

struct AgentAnswer
{
    bool success = false;
    std::string text;
    std::vector<std::string> citations;
    std::vector<std::string> trace;
};

class HealthcareAgent
{
public:
    HealthcareAgent(const TfidfVectorStore& store, HealthcareSafetyPolicy policy, ILanguageModelClient& client);
    AgentAnswer run(const std::string& question, std::size_t maxSteps = 4);

private:
    const TfidfVectorStore& store_;
    HealthcareSafetyPolicy policy_;
    ILanguageModelClient& client_;
};
}
