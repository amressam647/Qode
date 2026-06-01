
from langchain_openai import ChatOpenAI
from langchain_google_genai import ChatGoogleGenerativeAI
from langchain_ollama import ChatOllama
import os

class AgentCore:
    def __init__(self, provider="ollama", model_name="llama3", api_key=None, base_url=None):
        self.llm = self._create_llm(provider, model_name, api_key, base_url)

    def _create_llm(self, provider, model_name, api_key, base_url):
        if provider == "openai":
            return ChatOpenAI(
                model=model_name, 
                api_key=api_key, 
                base_url=base_url
            )
        elif provider == "google":
            return ChatGoogleGenerativeAI(
                model=model_name,
                google_api_key=api_key
            )
        elif provider == "ollama":
            return ChatOllama(
                model=model_name,
                base_url=base_url or "http://localhost:11434"
            )
        elif provider == "lm_studio":
            # LM Studio mimics OpenAI
            return ChatOpenAI(
                model=model_name or "local-model",
                api_key="lm-studio",
                base_url=base_url or "http://localhost:1234/v1"
            )
        else:
            raise ValueError(f"Unknown provider: {provider}")

    def chat(self, messages):
        # messages is list of {"role": "user", "content": "..."}
        # LangChain expects BaseMessage objects or similar
        # Simple invoke for now
        response = self.llm.invoke(messages[-1]["content"])
        return response.content
