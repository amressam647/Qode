# Model Routing System

Qode supports multiple AI providers and dynamically routes tasks to the most appropriate model.

---

## 🌐 Supported Providers

### Cloud Models
- OpenAI GPT models
- Google AI models

### Local Models
- Ollama
- LM Studio

---

## 🧠 Routing Strategy

Qode selects models based on:

- Agent role
- Task complexity
- Performance requirements
- Privacy constraints

---

## ⚙️ Example Routing

| Task | Model |
|------|------|
| Code generation | OpenAI GPT |
| Local offline task | Ollama |
| Fast reasoning | LM Studio |
| Research task | Google AI |

---

## 🔐 Privacy Modes

- Cloud Mode → uses external APIs
- Local Mode → fully offline execution
- Hybrid Mode → combines both

---

## 🎯 Goal

To ensure every agent uses the most suitable AI model for its role and task.
