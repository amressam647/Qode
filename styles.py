
import streamlit as st

def load_css():
    st.markdown("""
        <style>
            /* Main Background */
            .stApp {
                background-color: #0e0e0e;
                color: #cccccc;
            }
            
            /* Sidebar Background */
            [data-testid="stSidebar"] {
                background-color: #181818;
                border-right: 1px solid #2b2b2b;
            }
            
            /* Inputs */
            .stTextInput input {
                background-color: #252526;
                color: #cccccc;
                border: 1px solid #3c3c3c;
                border-radius: 4px;
            }
            .stTextInput input:focus {
                border-color: #007acc;
                box-shadow: none;
            }
            
            /* Buttons */
            .stButton button {
                background-color: #0e639c;
                color: white;
                border: none;
                border-radius: 2px;
                padding: 4px 12px;
            }
            .stButton button:hover {
                background-color: #1177bb;
            }
            
            /* Remove Streamlit Header/Footer */
            #MainMenu {visibility: hidden;}
            footer {visibility: hidden;}
            header {visibility: hidden;}
            
            /* Custom Tabs mimicking VS Code */
            .stTabs [data-baseweb="tab-list"] {
                gap: 2px;
                background-color: #181818;
            }
            .stTabs [data-baseweb="tab"] {
                height: 35px;
                background-color: #181818;
                border: none;
                color: #969696;
            }
            .stTabs [data-baseweb="tab"][aria-selected="true"] {
                background-color: #0e0e0e;
                color: white;
                border-top: 1px solid #007acc;
            }
            
            /* Code Editor look */
            code {
                font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
                background-color: #1e1e1e;
            }
        </style>
    """, unsafe_allow_html=True)
