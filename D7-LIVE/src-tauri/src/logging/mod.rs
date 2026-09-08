use std::sync::Once;
static INIT: Once = Once::new();
pub fn init(){INIT.call_once(||{let filter=tracing_subscriber::EnvFilter::try_from_default_env().unwrap_or_else(|_|"info".into());tracing_subscriber::fmt().with_env_filter(filter).init();});}
