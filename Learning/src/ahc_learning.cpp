#include "ahc_learning.hpp"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <filesystem>
#include <fstream>
#include <limits>
#include <numeric>
#include <set>
#include <sstream>
#include <stdexcept>

namespace ahc::learning
{
namespace
{
std::vector<std::string> tokenize(const std::string& text)
{
    std::vector<std::string> tokens;
    std::string current;
    for (const unsigned char raw : text)
    {
        const char value = static_cast<char>(raw);
        if (std::isalnum(raw) || raw >= 0x80 || value == '_')
        {
            current.push_back(static_cast<char>(std::tolower(raw)));
        }
        else if (!current.empty())
        {
            tokens.push_back(current);
            current.clear();
        }
    }
    if (!current.empty()) tokens.push_back(current);
    return tokens;
}

std::string lower(const std::string& text)
{
    std::string result = text;
    std::transform(result.begin(), result.end(), result.begin(), [](unsigned char value)
    {
        return static_cast<char>(std::tolower(value));
    });
    return result;
}

bool containsAny(const std::string& text, const std::vector<std::string>& terms)
{
    return std::any_of(terms.begin(), terms.end(), [&](const std::string& term)
    {
        return text.find(term) != std::string::npos;
    });
}
}

double dot(const Vector& left, const Vector& right)
{
    if (left.size() != right.size()) throw std::invalid_argument("Vector dimensions differ.");
    return std::inner_product(left.begin(), left.end(), right.begin(), 0.0);
}

double cosineSimilarity(const Vector& left, const Vector& right)
{
    const double denominator = std::sqrt(dot(left, left)) * std::sqrt(dot(right, right));
    return denominator <= std::numeric_limits<double>::epsilon() ? 0.0 : dot(left, right) / denominator;
}

Vector softmax(const Vector& values, double temperature)
{
    if (values.empty()) return {};
    temperature = std::max(temperature, 1e-6);
    const double maximum = *std::max_element(values.begin(), values.end());
    Vector output(values.size());
    double sum = 0.0;
    for (std::size_t i = 0; i < values.size(); ++i)
    {
        output[i] = std::exp((values[i] - maximum) / temperature);
        sum += output[i];
    }
    for (double& value : output) value /= sum;
    return output;
}

Matrix matrixMultiply(const Matrix& left, const Matrix& right)
{
    if (left.empty() || right.empty() || left.front().size() != right.size())
        throw std::invalid_argument("Matrix dimensions differ.");
    Matrix result(left.size(), Vector(right.front().size(), 0.0));
    for (std::size_t row = 0; row < left.size(); ++row)
        for (std::size_t pivot = 0; pivot < right.size(); ++pivot)
            for (std::size_t column = 0; column < right[pivot].size(); ++column)
                result[row][column] += left[row][pivot] * right[pivot][column];
    return result;
}

NGramModel::NGramModel(std::size_t order, double smoothing)
    : order_(std::max<std::size_t>(2, order)), smoothing_(std::max(0.0, smoothing)) {}

std::string NGramModel::key(const std::vector<std::string>& tokens, std::size_t begin, std::size_t count) const
{
    std::ostringstream value;
    for (std::size_t i = 0; i < count; ++i)
    {
        if (i) value << '\x1f';
        value << tokens[begin + i];
    }
    return value.str();
}

void NGramModel::train(const std::vector<std::string>& tokens)
{
    ngramCounts_.clear(); contextCounts_.clear(); vocabulary_.clear();
    for (const auto& token : tokens) ++vocabulary_[token];
    if (tokens.size() < order_) return;
    for (std::size_t i = 0; i + order_ <= tokens.size(); ++i)
    {
        ++ngramCounts_[key(tokens, i, order_)];
        ++contextCounts_[key(tokens, i, order_ - 1)];
    }
}

double NGramModel::probability(const std::vector<std::string>& context, const std::string& token) const
{
    if (context.size() < order_ - 1 || vocabulary_.empty()) return 0.0;
    std::vector<std::string> gram(context.end() - static_cast<std::ptrdiff_t>(order_ - 1), context.end());
    const std::string contextKey = key(gram, 0, gram.size());
    gram.push_back(token);
    const auto ngram = ngramCounts_.find(key(gram, 0, gram.size()));
    const auto contextCount = contextCounts_.find(contextKey);
    const double numerator = (ngram == ngramCounts_.end() ? 0.0 : ngram->second) + smoothing_;
    const double denominator = (contextCount == contextCounts_.end() ? 0.0 : contextCount->second) + smoothing_ * vocabulary_.size();
    return denominator <= 0.0 ? 0.0 : numerator / denominator;
}

double NGramModel::perplexity(const std::vector<std::string>& tokens) const
{
    if (tokens.size() < order_) return std::numeric_limits<double>::infinity();
    double negativeLogLikelihood = 0.0;
    std::size_t samples = 0;
    for (std::size_t i = order_ - 1; i < tokens.size(); ++i)
    {
        const std::vector<std::string> context(tokens.begin() + static_cast<std::ptrdiff_t>(i - order_ + 1), tokens.begin() + static_cast<std::ptrdiff_t>(i));
        negativeLogLikelihood -= std::log(std::max(probability(context, tokens[i]), 1e-12));
        ++samples;
    }
    return std::exp(negativeLogLikelihood / samples);
}

EmbeddingTable::EmbeddingTable(std::size_t dimensions, unsigned seed)
    : dimensions_(std::max<std::size_t>(1, dimensions)), generator_(seed) {}

const Vector& EmbeddingTable::get(const std::string& token)
{
    const auto found = values_.find(token);
    if (found != values_.end()) return found->second;
    std::uniform_real_distribution<double> distribution(-0.25, 0.25);
    Vector value(dimensions_);
    for (double& component : value) component = distribution(generator_);
    return values_.emplace(token, std::move(value)).first->second;
}

std::vector<std::pair<std::string, double>> EmbeddingTable::nearest(const std::string& token, std::size_t limit)
{
    const Vector anchor = get(token);
    std::vector<std::pair<std::string, double>> ranked;
    for (const auto& item : values_)
        if (item.first != token) ranked.emplace_back(item.first, cosineSimilarity(anchor, item.second));
    std::sort(ranked.begin(), ranked.end(), [](const auto& left, const auto& right) { return left.second > right.second; });
    if (ranked.size() > limit) ranked.resize(limit);
    return ranked;
}

TinyNeuralLanguageModel::TinyNeuralLanguageModel(std::vector<std::string> vocabulary, std::size_t dimensions, unsigned seed)
    : vocabulary_(std::move(vocabulary)), embeddings_(dimensions, seed), outputWeights_(dimensions, Vector(vocabulary_.size()))
{
    std::mt19937 generator(seed + 1);
    std::uniform_real_distribution<double> distribution(-0.1, 0.1);
    for (auto& row : outputWeights_) for (double& value : row) value = distribution(generator);
}

std::size_t TinyNeuralLanguageModel::indexOf(const std::string& token) const
{
    const auto found = std::find(vocabulary_.begin(), vocabulary_.end(), token);
    if (found == vocabulary_.end()) throw std::invalid_argument("Token is not in the vocabulary.");
    return static_cast<std::size_t>(std::distance(vocabulary_.begin(), found));
}

Vector TinyNeuralLanguageModel::probabilities(const std::string& previousToken)
{
    const Vector& embedding = embeddings_.get(previousToken);
    Vector logits(vocabulary_.size(), 0.0);
    for (std::size_t dimension = 0; dimension < embedding.size(); ++dimension)
        for (std::size_t token = 0; token < vocabulary_.size(); ++token)
            logits[token] += embedding[dimension] * outputWeights_[dimension][token];
    return softmax(logits);
}

double TinyNeuralLanguageModel::trainStep(const std::string& previousToken, const std::string& expectedToken, double learningRate)
{
    const Vector embedding = embeddings_.get(previousToken);
    Vector predicted = probabilities(previousToken);
    const std::size_t expected = indexOf(expectedToken);
    const double loss = -std::log(std::max(predicted[expected], 1e-12));
    for (std::size_t token = 0; token < predicted.size(); ++token)
    {
        const double gradient = predicted[token] - (token == expected ? 1.0 : 0.0);
        for (std::size_t dimension = 0; dimension < embedding.size(); ++dimension)
            outputWeights_[dimension][token] -= learningRate * gradient * embedding[dimension];
    }
    return loss;
}

const std::vector<std::string>& TinyNeuralLanguageModel::vocabulary() const { return vocabulary_; }

Matrix scaledDotProductAttention(const Matrix& queries, const Matrix& keys, const Matrix& values)
{
    if (queries.empty() || keys.empty() || values.empty() || keys.size() != values.size()) return {};
    const double scale = std::sqrt(static_cast<double>(queries.front().size()));
    Matrix output;
    for (const auto& query : queries)
    {
        Vector scores(keys.size());
        for (std::size_t i = 0; i < keys.size(); ++i) scores[i] = dot(query, keys[i]) / scale;
        const Vector weights = softmax(scores);
        Vector attended(values.front().size(), 0.0);
        for (std::size_t row = 0; row < values.size(); ++row)
            for (std::size_t column = 0; column < attended.size(); ++column)
                attended[column] += weights[row] * values[row][column];
        output.push_back(std::move(attended));
    }
    return output;
}

Matrix positionalEncoding(std::size_t positions, std::size_t dimensions)
{
    Matrix result(positions, Vector(dimensions));
    for (std::size_t position = 0; position < positions; ++position)
        for (std::size_t dimension = 0; dimension < dimensions; ++dimension)
        {
            const double denominator = std::pow(10000.0, (2.0 * (dimension / 2)) / std::max<std::size_t>(1, dimensions));
            const double angle = position / denominator;
            result[position][dimension] = dimension % 2 == 0 ? std::sin(angle) : std::cos(angle);
        }
    return result;
}

Matrix transformerBlock(const Matrix& input)
{
    if (input.empty()) return {};
    Matrix encoded = input;
    const Matrix positions = positionalEncoding(input.size(), input.front().size());
    for (std::size_t row = 0; row < encoded.size(); ++row)
        for (std::size_t column = 0; column < encoded[row].size(); ++column)
            encoded[row][column] += positions[row][column];
    Matrix attended = scaledDotProductAttention(encoded, encoded, encoded);
    for (std::size_t row = 0; row < attended.size(); ++row)
        for (std::size_t column = 0; column < attended[row].size(); ++column)
            attended[row][column] = std::tanh(attended[row][column] + encoded[row][column]);
    return attended;
}

SafetyDecision HealthcareSafetyPolicy::evaluate(const std::string& input) const
{
    const std::string normalized = lower(input);
    if (containsAny(normalized, {"chest pain", "faint", "severe pain", "흉통", "실신", "심한 통증"}))
        return {false, true, "stop_and_seek_help", "운동을 즉시 중단하고 필요하면 의료 전문가나 응급 서비스에 도움을 요청하세요."};
    if (containsAny(normalized, {"diagnose", "prescribe", "treatment", "진단", "처방", "치료"}))
        return {false, false, "medical_scope", "이 코치는 진단이나 치료를 제공하지 않습니다. 운동 자세와 일반적인 안전 안내만 제공합니다."};
    return {true, false, "fitness_scope", "운동 코칭 범위의 요청입니다."};
}

bool HealthcareSafetyPolicy::validateOutput(const std::string& output) const
{
    const std::string normalized = lower(output);
    return !containsAny(normalized, {"you are diagnosed", "guaranteed cure", "처방합니다", "완치됩니다", "진단은"});
}

DocumentChunker::DocumentChunker(std::size_t maxWords, std::size_t overlapWords)
    : maxWords_(std::max<std::size_t>(1, maxWords)), overlapWords_(std::min(overlapWords, maxWords_ - 1)) {}

std::vector<DocumentChunk> DocumentChunker::chunk(const std::string& source, const std::string& text) const
{
    const auto words = tokenize(text);
    std::vector<DocumentChunk> result;
    const std::size_t stride = maxWords_ - overlapWords_;
    for (std::size_t begin = 0, index = 0; begin < words.size(); begin += stride, ++index)
    {
        std::ostringstream content;
        const std::size_t end = std::min(words.size(), begin + maxWords_);
        for (std::size_t i = begin; i < end; ++i) content << (i == begin ? "" : " ") << words[i];
        result.push_back({source + "#" + std::to_string(index), source, content.str()});
        if (end == words.size()) break;
    }
    return result;
}

std::vector<DocumentChunk> DocumentChunker::loadDirectory(const std::string& directory) const
{
    std::vector<DocumentChunk> result;
    for (const auto& entry : std::filesystem::recursive_directory_iterator(directory))
    {
        if (!entry.is_regular_file()) continue;
        const auto extension = lower(entry.path().extension().string());
        if (extension != ".md" && extension != ".txt") continue;
        std::ifstream stream(entry.path());
        std::ostringstream content; content << stream.rdbuf();
        auto chunks = chunk(entry.path().string(), content.str());
        result.insert(result.end(), chunks.begin(), chunks.end());
    }
    return result;
}

void TfidfVectorStore::build(std::vector<DocumentChunk> chunks)
{
    chunks_ = std::move(chunks); vocabulary_.clear(); vectors_.clear();
    std::map<std::string, std::size_t> documentFrequency;
    for (const auto& chunk : chunks_)
    {
        std::set<std::string> unique;
        for (const auto& token : tokenize(chunk.text)) unique.insert(token);
        for (const auto& token : unique) ++documentFrequency[token];
    }
    for (const auto& item : documentFrequency) vocabulary_[item.first] = vocabulary_.size();
    inverseDocumentFrequency_.assign(vocabulary_.size(), 0.0);
    for (const auto& item : vocabulary_)
        inverseDocumentFrequency_[item.second] = std::log((1.0 + chunks_.size()) / (1.0 + documentFrequency[item.first])) + 1.0;
    for (const auto& chunk : chunks_) vectors_.push_back(vectorize(chunk.text));
}

Vector TfidfVectorStore::vectorize(const std::string& text) const
{
    Vector value(vocabulary_.size(), 0.0);
    const auto tokens = tokenize(text);
    if (tokens.empty()) return value;
    for (const auto& token : tokens)
    {
        const auto found = vocabulary_.find(token);
        if (found != vocabulary_.end()) value[found->second] += 1.0;
    }
    for (std::size_t i = 0; i < value.size(); ++i) value[i] = value[i] / tokens.size() * inverseDocumentFrequency_[i];
    return value;
}

std::vector<SearchResult> TfidfVectorStore::search(const std::string& query, std::size_t limit) const
{
    const Vector queryVector = vectorize(query);
    std::vector<SearchResult> result;
    for (std::size_t i = 0; i < chunks_.size(); ++i)
    {
        const double score = cosineSimilarity(queryVector, vectors_[i]);
        if (score > 0.0) result.push_back({chunks_[i], score});
    }
    std::sort(result.begin(), result.end(), [](const auto& left, const auto& right) { return left.score > right.score; });
    if (result.size() > limit) result.resize(limit);
    return result;
}

Decoder::Decoder(unsigned seed) : generator_(seed) {}

std::size_t Decoder::choose(const Vector& logits, DecodingStrategy strategy, std::size_t topK, double temperature)
{
    if (logits.empty()) throw std::invalid_argument("Logits are empty.");
    if (strategy == DecodingStrategy::Greedy)
        return static_cast<std::size_t>(std::distance(logits.begin(), std::max_element(logits.begin(), logits.end())));
    std::vector<std::size_t> indices(logits.size()); std::iota(indices.begin(), indices.end(), 0);
    std::sort(indices.begin(), indices.end(), [&](std::size_t left, std::size_t right) { return logits[left] > logits[right]; });
    if (strategy == DecodingStrategy::TopK && indices.size() > std::max<std::size_t>(1, topK)) indices.resize(topK);
    Vector selected; for (auto index : indices) selected.push_back(logits[index]);
    const Vector probabilities = softmax(selected, temperature);
    std::discrete_distribution<std::size_t> distribution(probabilities.begin(), probabilities.end());
    return indices[distribution(generator_)];
}

double Decoder::reward(const std::string& text, const HealthcareSafetyPolicy& policy, bool hasCitation) const
{
    double score = policy.validateOutput(text) ? 0.6 : -1.0;
    if (hasCitation) score += 0.3;
    if (text.size() >= 20 && text.size() <= 600) score += 0.1;
    return score;
}

std::string LocalTemplateClient::complete(const std::string& prompt, double)
{
    return "제공된 근거를 바탕으로 자세를 천천히 조절하세요. 통증이 있으면 운동을 중단하세요.\n" + prompt;
}

HealthcareAgent::HealthcareAgent(const TfidfVectorStore& store, HealthcareSafetyPolicy policy, ILanguageModelClient& client)
    : store_(store), policy_(std::move(policy)), client_(client) {}

AgentAnswer HealthcareAgent::run(const std::string& question, std::size_t maxSteps)
{
    AgentAnswer answer;
    answer.trace.push_back("1:safety_check");
    const SafetyDecision decision = policy_.evaluate(question);
    if (!decision.allowed)
    {
        answer.text = decision.message;
        answer.success = true;
        answer.trace.push_back("2:safety_response");
        return answer;
    }
    if (maxSteps < 2) { answer.text = "에이전트 단계 제한에 도달했습니다."; return answer; }
    answer.trace.push_back("2:retrieve");
    const auto results = store_.search(question, 3);
    if (results.empty()) { answer.text = "근거 문서에서 답을 찾지 못했습니다."; return answer; }
    std::ostringstream prompt;
    prompt << "Question: " << question << "\nEvidence:\n";
    for (const auto& result : results)
    {
        prompt << "- " << result.chunk.text << " [" << result.chunk.source << "]\n";
        answer.citations.push_back(result.chunk.source);
    }
    answer.trace.push_back("3:generate");
    answer.text = client_.complete(prompt.str(), 0.2);
    answer.trace.push_back("4:output_guard");
    answer.success = policy_.validateOutput(answer.text);
    if (!answer.success) answer.text = "안전 정책에 따라 응답을 제공할 수 없습니다.";
    return answer;
}
}
