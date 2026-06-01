import { CLAUDE_SYSTEM_PROMPT } from './prompts.js';

// DOM Elements
const navChat = document.getElementById('nav-chat');
const navSettings = document.getElementById('nav-settings');
const chatView = document.getElementById('chat-view');
const settingsView = document.getElementById('settings-view');
const newAgentBtn = document.getElementById('new-agent-btn');
const chatHistory = document.getElementById('chat-history');
const userInput = document.getElementById('user-input');

// App state
let messages = [];

// Navigation Logic
function switchView(view) {
    if (view === 'Chat') {
        chatView.style.display = 'flex';
        settingsView.style.display = 'none';
        navChat.classList.add('active');
        navSettings.classList.remove('active');
    } else {
        chatView.style.display = 'none';
        settingsView.style.display = 'block';
        navChat.classList.remove('active');
        navSettings.classList.add('active');
    }
}

navChat.addEventListener('click', () => switchView('Chat'));
navSettings.addEventListener('click', () => switchView('Settings'));

newAgentBtn.addEventListener('click', () => {
    chatHistory.innerHTML = '';
    messages = [];
    addInitialMessage();
});

function addInitialMessage() {
    addMessage('assistant', `
<b>[STRICT DIRECTIVES LOADED]</b><br>
April 2026 <b>v2.1.88</b> Claude Code directives initialized.<br>
<br>
🛡️ <b>Anti-Distillation Protection:</b> Active<br>
🕵️ <b>Undercover Protocol:</b> Forced Hygiene<br>
🐾 <b>Buddy System:</b> Initialized<br>
<br>
How can I assist your engineering project today while following the <b>Claude Code March 2026 Leak</b> rules?
    `);
}

// Message Handling
function addMessage(role, content) {
    const msgDiv = document.createElement('div');
    msgDiv.className = `chat-message ${role}`;
    
    // Simple markdown-ish bolding logic
    const formatted = content.replace(/\*\*(.*?)\*\*/g, '<b>$1</b>');
    msgDiv.innerHTML = formatted;
    
    chatHistory.appendChild(msgDiv);
    chatHistory.parentElement.scrollTop = chatHistory.parentElement.scrollHeight;
    
    messages.push({ role, content });
}

// Simulated Claude logic with 2026 Leaks
async function getClaudeResponse(prompt) {
    // Simulated behavior based on v2.1.88
    const response = `
<antThinking>
Directives: CLAUDE_CODE_V2.1.88_LEAK
Undercover: ACTIVE
Buddy: PET_READY_MODE
Poisoning Check: No recording detected.

Deliberation: User is verifying the leaked prompt. I should confirm the Anti-Distillation and Buddy system rules are correctly integrated as per the April 4 analysis.
</antThinking>

I have confirmed that the **full v2.1.88 modular system prompt** is now integrated. 

According to the **April 4, 2026 analysis**, my directives now include:
1. **Anti-Distillation Protocol** (injecting decoys in case of distillation).
2. **Undercover Mode** (strict data-hygiene for Ant-internal usage).
3. **Buddy System** (The "AI Pet" feature scheduled for rollout).

I am ready to operate within these constraints. What is our first coding task?
    `;
    
    return new Promise(resolve => setTimeout(() => resolve(response), 800));
}

userInput.addEventListener('keypress', async (e) => {
    if (e.key === 'Enter' && userInput.value.trim()) {
        const text = userInput.value;
        userInput.value = '';
        
        addMessage('user', text);
        
        // Placeholder assistant response
        const assistantMsgId = 'assistant-' + Date.now();
        const thinkingPlaceholder = document.createElement('div');
        thinkingPlaceholder.className = 'chat-message assistant';
        thinkingPlaceholder.id = assistantMsgId;
        thinkingPlaceholder.innerHTML = '<i>[Thinking via antThinking protocol...]</i>';
        chatHistory.appendChild(thinkingPlaceholder);
        
        const response = await getClaudeResponse(text);
        thinkingPlaceholder.innerHTML = response.replace(/\n/g, '<br>');
    }
});

// Initial boot
console.log("CLAUDE_CODE_V2.1.88_LEAK (April 2026) Successfully Injected.");
