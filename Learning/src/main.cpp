#include "ahc_learning.hpp"

#include <iostream>
#include <string>

int main()
{
    using namespace ahc::learning;
    DocumentChunker chunker(50, 8);
    TfidfVectorStore store;
    store.build(chunker.loadDirectory("../Assets/StreamingAssets/RagKnowledge"));
    HealthcareSafetyPolicy policy;
    LocalTemplateClient client;
    HealthcareAgent agent(store, policy, client);

    std::cout << "AI Healthcare Coach learning client (type quit to exit)\n";
    std::string input;
    while (std::cout << "> " && std::getline(std::cin, input) && input != "quit")
    {
        const AgentAnswer answer = agent.run(input);
        std::cout << answer.text << "\n";
        for (const auto& citation : answer.citations) std::cout << "  source: " << citation << "\n";
    }
    return 0;
}
