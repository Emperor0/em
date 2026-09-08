use serde::{Serialize,Deserialize};
#[derive(Debug,Clone,Serialize,Deserialize)] pub struct UpdateSettings{pub check_on_startup:bool,pub cadence:String,pub install_after_stream:bool}
impl Default for UpdateSettings{fn default()->Self{Self{check_on_startup:true,cadence:"daily".into(),install_after_stream:false}}}
