{
	if(e.headers.get('Content-Type')==='application/json'){
		let data=await e.json();
		if(data.subscription===null){
			const subscriptionPlan='yearly';
			const subscriptionState='active';
			const subscriptionTrial=false;
			const subscriptionGift=true;
			const subscriptionAmount=0;
			const dateYears=(years)=>{
				const d=new Date();
				d.setFullYear(d.getFullYear()+years);
				return d.toISOString();
			};
			const dateDays=(days)=>{
				const d=new Date();
				d.setDate(d.getDate()+days);
				return d.toISOString();
			};
			const startedAt=new Date('01/01/2009').toISOString();
			const trialEndsAt=dateDays(3);
			const endsAt=dateYears(1);
			let subscription={
				startedAt,
				endsAt,
				period:subscriptionPlan,
				state:subscriptionState,
				nextInvoice:{
					date:endsAt,
					amount:subscriptionAmount,
					currency:'EUR',
				},
			};
			if (subscriptionTrial) {
				subscription={
					...subscription,...{
						trialEndsAt
					}
				};
			}
			if (subscriptionGift) {
				subscription={
					...subscription,...{
						gift:{
							senderName:'Gift_Sender'
						}
					}
				};
			}
			data.subscription=subscription;
		}
		return data;
	}
	return await e.text();
}