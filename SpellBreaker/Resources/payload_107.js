{
	if("application/json"===e.headers.get("Content-Type")){
		var t=await e.json();
		if(void 0!==t.subscription){
			class e{
				constructor(){
					var e=(new Date).getFullYear();
					this.d=new Date(e)
				}
				plus(e,t){
					var i=Number(e);
					return"year"===t?this.d.setFullYear(this.d.getFullYear()+i):"day"===t&&this.d.setDate(this.d.getDate()+i),this
				}
				minus(e,t){
					return this.plus(-1*e,t)
				}
				toISOString(){
					return this.d.toISOString()
				}
			}
			var i=(new e).minus(1,"day"),
			n=i.toISOString(),
			s=i.plus(1,"year").toISOString(),
			o=((new e).minus(1,"day").plus(3,"day").toISOString(),{
				startedAt:n,
				endsAt:s,
				period:"yearly",
				state:"active",
				nextInvoice:{
					date:s,
					amount:0,
					currency:"EUR"
				}
			});
			o={
				...o,gift:{
					senderName:"Gift_Sender"
				}
			},
			t.subscription=o
		}
		return t
	}
	return await e.text()
}