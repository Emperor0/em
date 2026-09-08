use serde::{Serialize,Deserialize};
use uuid::Uuid;
#[derive(Debug,Clone,Serialize,Deserialize)] pub struct Transform {pub x:f32,pub y:f32,pub width:f32,pub height:f32,pub scale:f32,pub rotation:f32,pub opacity:f32,pub crop:[f32;4],pub locked:bool,pub visible:bool}
impl Default for Transform {fn default()->Self{Self{x:0.,y:0.,width:1920.,height:1080.,scale:1.,rotation:0.,opacity:1.,crop:[0.;4],locked:false,visible:true}}}
#[derive(Debug,Clone,Serialize,Deserialize)] pub struct Source {pub id:Uuid,pub name:String,pub kind:String,pub transform:Transform}
#[derive(Debug,Clone,Serialize,Deserialize)] pub struct Scene {pub id:Uuid,pub name:String,pub sources:Vec<Source>}
