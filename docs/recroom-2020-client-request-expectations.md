# Rec Room 2020 Client Request Expectations

Generated from `dist/RecRoom-2020.12.18-isil/IsilDump/Assembly-CSharp` and grouped by unique route literal or route fragment. The readable C# dump often has empty method bodies, so expected results below are inferred from route names, nearby DTO declarations, known DorkNet server behavior, and the client service family. Use the CSV for exact call-site file/line context.

Catalog CSV: `docs/recroom-2020-client-request-catalog.csv`

Unique route/fragments: 214; raw call-site rows: 367.

## account

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `account/{0}` | Account profile object or success result, depending on verb. | PEGGCEDHBOF:RecRoom.Async.IPromise`1<CCEOLAOLEKJ> NFKMGNHDCMN(System.Int32 GKLPIFBPGOD):PEGGCEDHBOF.txt:3934 |
| `account/{0}/bio` | Updated or fetched profile bio string/state; usually success object or account profile refresh. | PEGGCEDHBOF:RecRoom.Async.IPromise`1<FKNGFLFDIIB> GBLAOEFPDAH(System.Int32 GKLPIFBPGOD):PEGGCEDHBOF.txt:7866<br>PEGGCEDHBOF+<>c:System.Void <ChangeBio>b__67_0():PEGGCEDHBOF_NestedType___c.txt:587 |
| `account/{0}/clubs` | Account profile object or success result, depending on verb. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<PLILLKHMNDA>> KGGMKBADLDM(System.Int32 GKLPIFBPGOD):JDJGIBLMFKK.txt:9580 |
| `account/bulk` | Array/list of account profile summaries for requested ids. | PEGGCEDHBOF:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CCEOLAOLEKJ>> NHCHADDMPFC(System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG):PEGGCEDHBOF.txt:4917 |
| `account/bulk?` | Array/list of account profile summaries for requested ids. | PEGGCEDHBOF:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CCEOLAOLEKJ>> NHCHADDMPFC(System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG):PEGGCEDHBOF.txt:4963 |
| `account/bulk/` | Array/list of account profile summaries for requested ids. | PEGGCEDHBOF:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<DCGAMCKNJDB>> NGENLHMLHNG(System.Collections.Generic.List`1<System.String> BGELOMIEKFK, System.String MAFJOJLDFJO, System.String LOOAPLFGOEN, System.String PJIBCCFFMNJ, GPOOLJODEGM OLBDHLLJJHF):PEGGCEDHBOF.txt:9080 |
| `account/create` | Created account/player profile object plus login/account state; errors for duplicate or invalid names. | PEGGCEDHBOF:RecRoom.Async.IPromise`1<CCEOLAOLEKJ> HHFPNFANNEG():PEGGCEDHBOF.txt:5418 |
| `account/me` | Current account/player profile object for the authenticated player. | PEGGCEDHBOF:RecRoom.Async.IPromise`1<JJGHAFKJBEI> HJFCFNCKDFO():PEGGCEDHBOF.txt:3576 |
| `account/me/` | Account profile object or success result, depending on verb. | PEGGCEDHBOF:RecRoom.Async.IPromise OEBEELJDOHE(BestHTTP.HTTPMethods APAICGIHAGJ, BestHTTP.Forms.HTTPUrlEncodedForm MDOPLMHIKLP, System.String LOOAPLFGOEN):PEGGCEDHBOF.txt:6704 |
| `account/me/changepassword` | Boolean/success result for password status/change/recovery. | MHOKOMMOGKM:RecRoom.Async.IPromise PGJBIIPMMJP(BestHTTP.Forms.HTTPUrlEncodedForm MDOPLMHIKLP, System.String MKIPICKCFDM):MHOKOMMOGKM.txt:2582 |
| `account/me/haspassword` | Boolean/success result for password status/change/recovery. | MHOKOMMOGKM:RecRoom.Async.IPromise`1<System.Boolean> PPKFJEINCCN():MHOKOMMOGKM.txt:2203<br>MHOKOMMOGKM+<>c:System.Void <CreatePassword>b__32_0():MHOKOMMOGKM_NestedType___c.txt:584 |
| `account/recoverpassword` | Boolean/success result for password status/change/recovery. | MHOKOMMOGKM:RecRoom.Async.IPromise PMKEHDEDNCK(System.String LJKKKECANAB):MHOKOMMOGKM.txt:2779 |
| `account/search?name=` | Array/list of matching account profile summaries. | PEGGCEDHBOF:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CCEOLAOLEKJ>> IBDGAKHBIAJ(System.String CNBKKCJAHPP):PEGGCEDHBOF.txt:9372 |

## activities

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `api/activities/charades/v1/words` | Activity-specific DTO/list, challenge/royale state, or charades word list. | CardBox:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<JOHPEJAMBHG>> HMNGGFKCDGD():CardBox.txt:2422<br>CardBox:System.Void Start():CardBox.txt:2221 |
| `api/challenge/` | Activity-specific DTO/list, challenge/royale state, or charades word list. | PDMJMMMNGJE:System.Collections.IEnumerator EOBJGALHFNP(BPHGKAEDBPE+OJDJIJDNFHE<FNEEJMCEPOL> AFLPGGJMPOE):PDMJMMMNGJE.txt:156<br>PDMJMMMNGJE:System.Void EKJPOJFLGFO(System.Int32 DKDDEIHEIBL, COLBJJAIEIO FMIIMOCIHCD):PDMJMMMNGJE.txt:395 |
| `api/royale/` | Activity-specific DTO/list, challenge/royale state, or charades word list. | JDFDLJJGHIP:RecRoom.Async.IPromise`1<JDFDLJJGHIP+GHNGAENLIHA> APOAEDMOIHO():JDFDLJJGHIP.txt:266<br>JDFDLJJGHIP:RecRoom.Async.IPromise`1<JDFDLJJGHIP+JLNAMKBKPNG> NGEFKCDJAMF(JDFDLJJGHIP+MatchCompleteStats CNALBEPOKJJ):JDFDLJJGHIP.txt:477 |

## avatar

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `{0}v2/gifts/consume/` | Gift consumption success plus updated avatar/inventory/gift state. | NLEKGNENMCO:RecRoom.Async.IPromise PCKICLJFOBO(NLEKGNENMCO+LOCNECLOHCA HBBAANIPIMP, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE):NLEKGNENMCO.txt:2488 |
| `api/avatar/` | Avatar item/outfit/gift DTO or success result. | NLEKGNENMCO:RecRoom.Async.IPromise DLDENNPGADN():NLEKGNENMCO.txt:3480<br>NLEKGNENMCO:RecRoom.Async.IPromise PCKICLJFOBO(NLEKGNENMCO+LOCNECLOHCA HBBAANIPIMP, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE):NLEKGNENMCO.txt:2486<br>NLEKGNENMCO:RecRoom.Async.IPromise`1<NLEKGNENMCO+LOCNECLOHCA> JEKOPJCCHKB(GiftManager+LCLKAFOPBLD LHOMKMINCHH, System.Nullable`1<GiftManager+LCLKAFOPBLD> PCCLFNLJAMG, System.Boolean DKHOCHIJLBG):NLEKGNENMCO.txt:1504 |
| `api/avatar/v1/lockeditems?` | List of locked/unavailable avatar item ids. | NLEKGNENMCO:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<NLEKGNENMCO+EPFHLDCPAOK>> KLPMNFCFOLE(System.Collections.Generic.List`1<System.String> EGLBFNGKDLD):NLEKGNENMCO.txt:2931 |

## bug-reporting

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `api/bugreporting/` | Bug report submission success DTO. | RecNet.BugReporting:RecRoom.Async.IPromise ReportBug(System.String summary, System.String description, System.Byte[] screenshotData, System.Byte[] outputLogData, System.String testCaseKey):RecNet\BugReporting.txt:146 |

## clubs

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `{0}/club/{1}` | Club DTO/list or membership mutation success. | GNPDMBPGHBH:System.String JDEIGGFCBPF(System.Int64 AOMBLGBCENO):GNPDMBPGHBH.txt:630 |
| `announcements/club/{0}` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<HPACLJHLHBG> ONCKANJNGLA(System.Int64 AOMBLGBCENO):JDJGIBLMFKK.txt:1167<br>JDJGIBLMFKK:RecRoom.Async.IPromise`1<System.Int64> AAOLBAJFCOJ(JDJGIBLMFKK+DHPONMGBFJE JMFLHIIJFKL):JDJGIBLMFKK.txt:398 |
| `announcements/club/{0}/{1}` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise HCGFMHLEDPE(System.Int64 AOMBLGBCENO, System.Int64 LOHFFDEGIMK):JDJGIBLMFKK.txt:2462<br>JDJGIBLMFKK:RecRoom.Async.IPromise POHLPICNHJD(JDJGIBLMFKK+FOEPIDJCLMC JMFLHIIJFKL):JDJGIBLMFKK.txt:789 |
| `announcements/club/{0}/{1}/read` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise OFBNAHIOHON(System.Int64 AOMBLGBCENO, System.Int64 LOHFFDEGIMK):JDJGIBLMFKK.txt:2865 |
| `api/clubreporting/v1/report` | Report submission success. | JDJGIBLMFKK:RecRoom.Async.IPromise LBOOMIDNLPP(System.Int64 AOMBLGBCENO, LCCEEFHOBEN+CJFENPHAAHI MEABFEIBEMP, System.String EFDBFLPKHKA):JDJGIBLMFKK.txt:21791 |
| `club/{0}` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise IGHDIAEPHJD(System.Int64 AOMBLGBCENO, System.String DHANCEHHIDH):JDJGIBLMFKK.txt:13088<br>JDJGIBLMFKK:RecRoom.Async.IPromise`1<PLILLKHMNDA> NKGOFNILGPL(System.Int64 AOMBLGBCENO, System.Boolean OHLIGBELLLH = True):JDJGIBLMFKK.txt:10425 |
| `club/{0}/additionalimage/{1}` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<PIHMJGCGNLP> ELLIKHHEJFM(System.Int64 AOMBLGBCENO, System.Int32 EFBDCIJMFGD, System.String HFLPBHHAFIO):JDJGIBLMFKK.txt:12298 |
| `club/{0}/clubhouse` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<PIHMJGCGNLP> HELJLMINDFD(System.Int64 AOMBLGBCENO, System.Nullable`1<System.Int64> HNHLJONGKHB):JDJGIBLMFKK.txt:12449 |
| `club/{0}/details` | Club DTO/list or membership mutation success. | JDJGIBLMFKK+BKCHBCIJHBN:RecRoom.Async.IPromise`1<PIHMJGCGNLP> <GetClubDetailsById>b__0():JDJGIBLMFKK_NestedType_BKCHBCIJHBN.txt:202 |
| `club/{0}/mainimage` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<PIHMJGCGNLP> OBFKAFGALDM(System.Int64 AOMBLGBCENO, System.String HFLPBHHAFIO):JDJGIBLMFKK.txt:12138 |
| `club/{0}/members/{1}` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<MFOAODGNGKB> BIOJPNFPNCE(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD):JDJGIBLMFKK.txt:15776 |
| `club/{0}/members/acceptinvite` | Club membership mutation success or updated member DTO/list. | JDGDFALBCDJ:RecRoom.Async.IPromise HHLPPOBHMOC():JDGDFALBCDJ.txt:426<br>JDJGIBLMFKK:RecRoom.Async.IPromise FAOINNCBNFC(System.Int64 AOMBLGBCENO):JDJGIBLMFKK.txt:20586 |
| `club/{0}/members/acceptrequest` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise AGEGBKKPJNN(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD):JDJGIBLMFKK.txt:18418 |
| `club/{0}/members/acceptrequests` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise FKPIMLCILLI(System.Int64 AOMBLGBCENO, System.Collections.Generic.IEnumerable`1<System.Int32> ILNGMAANNDG):JDJGIBLMFKK.txt:18831 |
| `club/{0}/members/ban` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise OKICAHCMPFL(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD):JDJGIBLMFKK.txt:21265 |
| `club/{0}/members/changetype` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise JLBCCEIBFJJ(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD, PPGPAHNMGEC OCHEGLOFMEA):JDJGIBLMFKK.txt:21094 |
| `club/{0}/members/declineinvite` | Club membership mutation success or updated member DTO/list. | JDGDFALBCDJ:RecRoom.Async.IPromise BDFBMBCKJEP():JDGDFALBCDJ.txt:697<br>JDJGIBLMFKK:RecRoom.Async.IPromise GJJBKCEMLDC(System.Int64 AOMBLGBCENO):JDJGIBLMFKK.txt:20675 |
| `club/{0}/members/denyrequest` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise LPCHODIHIBB(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD):JDJGIBLMFKK.txt:19048 |
| `club/{0}/members/denyrequests` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise CJBOECCIIJB(System.Int64 AOMBLGBCENO, System.Collections.Generic.IEnumerable`1<System.Int32> ILNGMAANNDG):JDJGIBLMFKK.txt:19461 |
| `club/{0}/members/directJoin` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise AJCDOLIBBKC(System.Int64 AOMBLGBCENO, PJCALHOPMKJ IEGCAIGJBBP, System.Int32 ENGEEKIMIGO):JDJGIBLMFKK.txt:20450 |
| `club/{0}/members/invite` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise FEIADJMHICD(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD, PPGPAHNMGEC PGMGFKOCEDG):JDJGIBLMFKK.txt:19703 |
| `club/{0}/members/invitemembers` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise HOHPKNPHCFK(System.Int64 AOMBLGBCENO, System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG, OFIEEDOMGPA NAMECJCFEDN):JDJGIBLMFKK.txt:20070 |
| `club/{0}/members/leave` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise EBADBEIGOEN(System.Int64 AOMBLGBCENO):JDJGIBLMFKK.txt:20745 |
| `club/{0}/members/remove` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise LDOGPIHJOLP(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD):JDJGIBLMFKK.txt:20895 |
| `club/{0}/members/requesttojoin` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise NJKBIMEEAPE(System.Int64 AOMBLGBCENO):JDJGIBLMFKK.txt:18249 |
| `club/{0}/members/unban` | Club membership mutation success or updated member DTO/list. | JDJGIBLMFKK:RecRoom.Async.IPromise OGMGOBHPMFC(System.Int64 AOMBLGBCENO, System.Int32 GKLPIFBPGOD):JDJGIBLMFKK.txt:21435 |
| `club/{0}/modify` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<PIHMJGCGNLP> CKJIKPOIMFE(JDJGIBLMFKK+ACNILMIFDJJ JMFLHIIJFKL):JDJGIBLMFKK.txt:11620 |
| `club/{0}/modifydetails` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<PIHMJGCGNLP> KOPAANDJNKB(JDJGIBLMFKK+FHFOCPGBMPB JMFLHIIJFKL):JDJGIBLMFKK.txt:12001 |
| `club/{0}/permissions/{1}` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<PIHMJGCGNLP> GHBJEDCEKLK(JHEEFBMODPG OBJEOMAGODL):JDJGIBLMFKK.txt:13631 |
| `club/account/{0}/created` | List/page of clubs related to an account. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<PLILLKHMNDA>> DIAPLKMFFKO(System.Int32 GKLPIFBPGOD):JDJGIBLMFKK.txt:9416<br>JDJGIBLMFKK:System.String JANFGLBCBMJ(System.Int32 GKLPIFBPGOD):JDJGIBLMFKK.txt:7880<br>JDJGIBLMFKK:System.Void PJBLNGJKLEB(System.Int64 AOMBLGBCENO):JDJGIBLMFKK.txt:26175 |
| `club/categoryTags` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<System.String>> PAKNBFOJENP():JDJGIBLMFKK.txt:13314 |
| `club/create` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<PIHMJGCGNLP> KMPJFFBGJMD(JDJGIBLMFKK+ACNILMIFDJJ JMFLHIIJFKL):JDJGIBLMFKK.txt:11381 |
| `club/home/me` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<PLILLKHMNDA> DBDOOMCNNFE():JDJGIBLMFKK.txt:10003<br>JDJGIBLMFKK+<>c:System.Void <SetMyHomeClub>b__106_0():JDJGIBLMFKK_NestedType___c.txt:995<br>JDJGIBLMFKK+<>c:System.Void <SetMyHomeClub>b__106_2():JDJGIBLMFKK_NestedType___c.txt:954 |
| `club/mine/created` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<PLILLKHMNDA>> LJJLGJIMJED():JDJGIBLMFKK.txt:9275<br>JDJGIBLMFKK:System.Void PJBLNGJKLEB(System.Int64 AOMBLGBCENO):JDJGIBLMFKK.txt:26134<br>JDJGIBLMFKK:System.Void PJBLNGJKLEB(System.Int64 AOMBLGBCENO):JDJGIBLMFKK.txt:26326 |
| `club/mine/member` | Club DTO/list or membership mutation success. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<PLILLKHMNDA>> DFDMGNBKEKO():JDJGIBLMFKK.txt:9817<br>JDJGIBLMFKK:System.Void PJBLNGJKLEB(System.Int64 AOMBLGBCENO):JDJGIBLMFKK.txt:26194<br>JDJGIBLMFKK:System.Void PJBLNGJKLEB(System.Int64 AOMBLGBCENO):JDJGIBLMFKK.txt:26330 |
| `members/bulk` | Bulk member/account membership DTO list. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<JHMMGLNJHIB>> DDIJPHDEDDM(System.Int64 AOMBLGBCENO, System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG):JDJGIBLMFKK.txt:16847 |
| `members/bulk?` | Bulk member/account membership DTO list. | JDJGIBLMFKK:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<JHMMGLNJHIB>> DDIJPHDEDDM(System.Int64 AOMBLGBCENO, System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG):JDJGIBLMFKK.txt:16893 |

## config-settings

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `/config/{0}` | Config DTO/object for requested key. | FHLGJDFHOKL:RecRoom.Async.IPromise MCDNGDMJHBE():FHLGJDFHOKL.txt:502 |
| `api/config/` | Config DTO/object for requested key. | BBEFMHAEEEA:RecRoom.Async.IPromise BGNDHOHFJBN():BBEFMHAEEEA.txt:950<br>BBEFMHAEEEA:RecRoom.Async.IPromise OHHAEMHMIFE():BBEFMHAEEEA.txt:655<br>BBEFMHAEEEA:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<FPDADCNKELI>> EAJPLMPNPIJ(System.Int32 CKEBCMEKCJH):BBEFMHAEEEA.txt:1147 |
| `api/config/v1/freegiftbutton` | Config DTO/object for requested key. | BBEFMHAEEEA:RecRoom.Async.IPromise`1<System.Boolean> GMOMCLADPII():BBEFMHAEEEA.txt:1230 |
| `api/gameconfigs/` | Config DTO/object for requested key. | IGCCFMFHBBN:System.Void .cctor():IGCCFMFHBBN.txt:1921 |
| `api/settings/` | Player settings DTO or mutation success. | JFDILELKPAL:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<OHLKCDKEGMN>> HOHPJKNPEGK():JFDILELKPAL.txt:48<br>JFDILELKPAL:System.Collections.IEnumerator LHGBAFBJAHM(OHLKCDKEGMN AOKDECDAEEG, BPHGKAEDBPE+OJDJIJDNFHE<JFDILELKPAL+AMPGJPPJJBF> AFLPGGJMPOE):JFDILELKPAL.txt:224 |

## economy

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `{0}v1/consume` | Consumable/pageview consumption success. | FOEHJBDMMBH:System.Collections.IEnumerator LCELAOEOIJO(OOCCCGPICOG BAJGNABECMN, System.Int32 FKKAPHAMPMG, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE):FOEHJBDMMBH.txt:840 |
| `/consume` | Consumable/pageview consumption success. | COFHGNFJMOG:RecRoom.Async.IPromise`1<PLDFOPCHHJG> HDLJGFEDGLH(System.String ENJEOLBEALP, System.Nullable`1<System.Int32> LNBJKLOINED):COFHGNFJMOG.txt:1242 |
| `api/gamerewards/v1/pending` | List of pending reward choices. | COICJCJBABL:RecRoom.Async.IPromise AOJJODPOJFO():COICJCJBABL.txt:520 |
| `api/gamerewards/v1/select` | Reward selection success plus granted inventory/currency item. | COICJCJBABL:RecRoom.Async.IPromise ONNNOAKDIIP(COICJCJBABL+HNJGHCGJJFC FDAEIMEHDJJ, System.Int32 IAAKEHDHCAC):COICJCJBABL.txt:1055 |
| `pageview/consume` | Consumable/pageview consumption success. | COFHGNFJMOG:RecRoom.Async.IPromise`1<MPHABHIMOOO> LFHFLODHPJJ():COFHGNFJMOG.txt:95 |

## elo

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `api/PlayerElo/` | Player ELO/rating update or lookup DTO. | RecNet.Elo:System.Void UpdatePlayersElo(RecNet.Elo+PlayersEloUpdateDTO JFKDDPJDCDC):RecNet\Elo.txt:141 |

## equipment

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `api/equipment/` | Equipment/loadout DTO or mutation success. | ECINAMCDBJO:System.Void EEDJOJINECJ():ECINAMCDBJO.txt:468<br>ECINAMCDBJO+GPBDCOCADGE:System.Boolean MoveNext():ECINAMCDBJO_NestedType_GPBDCOCADGE.txt:147 |

## groups

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `api/groups/` | Group DTO/list or group membership mutation success. | EJECIMCPGMG:RecRoom.Async.IPromise`1<OGKIDDEAFND> CCDHCLAOFOJ(System.Int64 BCNAOOIPEJO):EJECIMCPGMG.txt:337<br>EJECIMCPGMG:RecRoom.Async.IPromise`1<OGKIDDEAFND> PDJKECHIHNP(System.Int64 CJFGEMGOJHB):EJECIMCPGMG.txt:555<br>EJECIMCPGMG:System.Collections.IEnumerator JAHJIFFICHN(System.String NDNLEGKJGCD, System.String LJIGOCDPEJF, System.String HFLPBHHAFIO, BPHGKAEDBPE+OJDJIJDNFHE<EJECIMCPGMG+CreateModifyGroupResponse> AFLPGGJMPOE):EJECIMCPGMG.txt:803 |

## images

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `api/images/` | Image upload/detail/list result; image binary is later fetched from image/CDN/storage URL. | OHDHPENHDAP:RecRoom.Async.IPromise BFHNENPOEFB(System.Int64 LKNNMPCBCKM, System.Boolean JBAGLLBLEEN):OHDHPENHDAP.txt:1571<br>OHDHPENHDAP:RecRoom.Async.IPromise FEOMMGBJFIN(System.Int64 LKNNMPCBCKM):OHDHPENHDAP.txt:2387<br>OHDHPENHDAP:RecRoom.Async.IPromise OFEBNGPIJMB():OHDHPENHDAP.txt:4467 |

## inventions

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `api/inventions/` | Invention DTO or mutation success. | BBHENFCNLAB:RecRoom.Async.IPromise`1<AHEPPAEOLOD> ANJHOOPIAKM(System.Int64 OEMDIAHHILF, System.Boolean JBAGLLBLEEN):BBHENFCNLAB.txt:7610<br>BBHENFCNLAB:RecRoom.Async.IPromise`1<AHEPPAEOLOD> BEPNJCGNIKA(System.Int64 OEMDIAHHILF, HECIICKPCDN AEHDODIANMG, System.Nullable`1<System.Int32> MACNIENMFHJ = null):BBHENFCNLAB.txt:6169<br>BBHENFCNLAB:RecRoom.Async.IPromise`1<AHEPPAEOLOD> HDFICFBNFOK(System.Int64 OEMDIAHHILF, System.Int32 MACNIENMFHJ):BBHENFCNLAB.txt:6479 |
| `api/inventions/v1/fulllineageowner?` | Boolean/ownership lineage result. | BBHENFCNLAB:RecRoom.Async.IPromise`1<System.Boolean> DEOOEGHNIAJ(System.Collections.Generic.List`1<System.Int64> POGCOENDJDJ):BBHENFCNLAB.txt:7823 |
| `api/inventions/v3/addversion` | Saved invention/version DTO containing invention id, version/data/image blob names, costs, ownership, and permission metadata. | BBHENFCNLAB+OIJIEFMJMKO:RecRoom.Async.IPromise`1<AHEPPAEOLOD> <AddInventionVersion>b__0(System.String filename):BBHENFCNLAB_NestedType_OIJIEFMJMKO.txt:216 |
| `api/inventions/v4/save` | Saved invention/version DTO containing invention id, version/data/image blob names, costs, ownership, and permission metadata. | BBHENFCNLAB+FODDLIKPKPE:RecRoom.Async.IPromise`1<AHEPPAEOLOD> <UploadNewInvention>b__0(System.String filename):BBHENFCNLAB_NestedType_FODDLIKPKPE.txt:250 |

## matchmaking

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `goto/club/{0}` | Room instance/join response: roomInstanceId, roomId, subRoomId, location, photonRegionId, photonRoomId, maxCapacity, isFull, isPrivate, isInProgress. | RecNet.Matchmaking:RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> IIDEDGKAGOE(PLILLKHMNDA EGLGJIONCCP, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False):RecNet\Matchmaking.txt:8847 |
| `goto/code/` | Room instance/join response: roomInstanceId, roomId, subRoomId, location, photonRegionId, photonRoomId, maxCapacity, isFull, isPrivate, isInProgress. | RecNet.Matchmaking:RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> FBLFCNDIPMP(System.String BFGCDJFNJLE, System.String KIIFGNILJEA, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = True, System.Boolean PBCPIEHFDBH = False):RecNet\Matchmaking.txt:8226 |
| `goto/event/{0}` | Room instance/join response: roomInstanceId, roomId, subRoomId, location, photonRegionId, photonRoomId, maxCapacity, isFull, isPrivate, isInProgress. | RecNet.Matchmaking:RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> MLIILFNOOOK(CCMBKDINCAH IAALIIFNHNP, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False):RecNet\Matchmaking.txt:8737 |
| `goto/instance/{0}` | Room instance/join response: roomInstanceId, roomId, subRoomId, location, photonRegionId, photonRoomId, maxCapacity, isFull, isPrivate, isInProgress. | RecNet.Matchmaking:RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> MGKFDNPCDBP(System.Int64 ANAPBECHGLI, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False, System.String APJJOJMDLNP = null):RecNet\Matchmaking.txt:9156 |
| `goto/invite/{0}` | Room instance/join response: roomInstanceId, roomId, subRoomId, location, photonRegionId, photonRoomId, maxCapacity, isFull, isPrivate, isInProgress. | RecNet.Matchmaking:RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> ALCJNMAOIGH(System.Int64 IAHDKAIJJLB, System.Int64 HNHLJONGKHB, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False):RecNet\Matchmaking.txt:9294 |
| `goto/none` | Room instance/join response: roomInstanceId, roomId, subRoomId, location, photonRegionId, photonRoomId, maxCapacity, isFull, isPrivate, isInProgress. | RecNet.Matchmaking:RecRoom.Async.IPromise OAHNGAMAPNB():RecNet\Matchmaking.txt:10314 |
| `goto/player/{0}` | Room instance/join response: roomInstanceId, roomId, subRoomId, location, photonRegionId, photonRoomId, maxCapacity, isFull, isPrivate, isInProgress. | RecNet.Matchmaking:RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> IMMCGNNPIAD(System.Int32 CJFGEMGOJHB, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False):RecNet\Matchmaking.txt:8980 |
| `goto/playlist/` | Room instance/join response: roomInstanceId, roomId, subRoomId, location, photonRegionId, photonRoomId, maxCapacity, isFull, isPrivate, isInProgress. | RecNet.Matchmaking:RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> NDKKNPMAGNK(System.String APJJOJMDLNP, System.Boolean OFFEBBLOEJA = False, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False):RecNet\Matchmaking.txt:8050 |
| `goto/room/` | Room instance/join response: roomInstanceId, roomId, subRoomId, location, photonRegionId, photonRoomId, maxCapacity, isFull, isPrivate, isInProgress. | RecNet.Matchmaking:RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> GDCFFOJEIMD():RecNet\Matchmaking.txt:7509<br>RecNet.Matchmaking:RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> ILPIDHICKMB(System.String BFGCDJFNJLE, System.Boolean OFFEBBLOEJA = False, System.Int32[] DKFBAOLAEFE = null, System.Boolean KJHCANCBKAL = False, System.String APJJOJMDLNP = null, System.Boolean PBCPIEHFDBH = False):RecNet\Matchmaking.txt:8426<br>RecNet.Matchmaking:RecRoom.Async.IPromise`1<RecNet.Matchmaking+MHCKNNJOIIP> JLMFIHJLNBL(System.String HFDPAGDDNDE, System.Int32[] DKFBAOLAEFE, System.Boolean PBCPIEHFDBH, System.Boolean KJHCANCBKAL, System.Boolean ALOAFENOBGN):RecNet\Matchmaking.txt:7882 |
| `room/{0}/instances` | List of active room instances with Photon and capacity/state fields. | RecNet.Matchmaking:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<JHNOAKDOMHG>> KFMFHGLEGHF(System.Int64 HNHLJONGKHB):RecNet\Matchmaking.txt:10726 |

## messages

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `api/messages/` | Message/thread DTO/list or mutation success. | KEBJPIGKGOI:RecRoom.Async.IPromise JHJDMMLABCG():KEBJPIGKGOI.txt:1297<br>KEBJPIGKGOI:RecRoom.Async.IPromise MPLDGJCCIMN(System.Int64 CJFGEMGOJHB, IGCIBGKPPMO+BBELBJELLHN JMDIPDGMIOG, System.String ABADFLCBFIJ, System.Nullable`1<System.Int64> HNHLJONGKHB):KEBJPIGKGOI.txt:2653<br>KEBJPIGKGOI:RecRoom.Async.IPromise`1<DIBODMEJOPN> IIHKDEGDMEA():KEBJPIGKGOI.txt:6118 |
| `api/messages/v1/IOSClearDeviceToken` | Primitive success result for device token/preferences mutation. | KEBJPIGKGOI:RecRoom.Async.IPromise LLIHILNLNEP():KEBJPIGKGOI.txt:5886 |
| `api/messages/v1/IOSModifyNotificationPreferences` | Primitive success result for device token/preferences mutation. | KEBJPIGKGOI:RecRoom.Async.IPromise NDONIOINHAE(DIBODMEJOPN JFMOEGCPNAC):KEBJPIGKGOI.txt:6455 |
| `api/messages/v1/IOSResetNotificationPreferencesBadgeCount` | Primitive success result for device token/preferences mutation. | KEBJPIGKGOI:RecRoom.Async.IPromise GENJNKHDHCN():KEBJPIGKGOI.txt:6347 |
| `api/messages/v1/IOSSaveDeviceToken` | Primitive success result for device token/preferences mutation. | KEBJPIGKGOI:RecRoom.Async.IPromise INNJDBLBCEO(System.String EAEICOOGLAK):KEBJPIGKGOI.txt:5768 |
| `api/messages/v3/delete` | Delete success result for message ids. | KEBJPIGKGOI:System.Collections.IEnumerator PEIBHPIEKGF(System.Collections.Generic.IEnumerable`1<System.Int64> OCCKKEIMPGP, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE = null):KEBJPIGKGOI.txt:3138 |
| `api/offlineinvite/` | Offline invite DTO/list or success result. | KEBJPIGKGOI:System.Collections.IEnumerator LPPPAHLCPCO(System.Int64 CJFGEMGOJHB, BPHGKAEDBPE+OJDJIJDNFHE<System.String> AFLPGGJMPOE):KEBJPIGKGOI.txt:6606 |

## misc

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `{0}v1/bulkignoreplatformusers` | Unknown/route-fragment result; inspect continuation for exact DTO. | FGPIDGLCKEF:System.Void IMLJEBCMMKK(PlatformManager+FCIKKFJOMNO FPMCLJDEGKL, System.Collections.Generic.List`1<System.UInt64> CIBFMCPBLEO):FGPIDGLCKEF.txt:2834 |
| `/bulk` | Unknown/route-fragment result; inspect continuation for exact DTO. | GNPDMBPGHBH:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CCMBKDINCAH>> DMHOIAJGABC(System.Collections.Generic.IReadOnlyList`1<System.Int64> JOGAGJIPEDN):GNPDMBPGHBH.txt:2388<br>GNPDMBPGHBH+MKCNPHKGFDM:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CCMBKDINCAH>> <GetEventsByIds>b__1(System.String uri):GNPDMBPGHBH_NestedType_MKCNPHKGFDM.txt:138 |
| `/data/{0}` | Unknown/route-fragment result; inspect continuation for exact DTO. | KELEPAPMOGK+CJACMOFOFKJ:RecRoom.Async.IPromise`1<KELEPAPMOGK+LEKKFHBPBAB> <GetData>b__0():KELEPAPMOGK_NestedType_CJACMOFOFKJ.txt:187 |
| `/room/` | Unknown/route-fragment result; inspect continuation for exact DTO. | OJMCBOKJFOF+BPPECKDOINI:RecRoom.Async.IPromise`1<System.Byte[]> <GetRoomData>b__0():OJMCBOKJFOF_NestedType_BPPECKDOINI.txt:206 |
| `api/announcement/v1/get` | Announcement DTO/list. | NLGOJMONPKG:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<MKEKHKEHPLO>> ADEEFOCPDFH():NLGOJMONPKG.txt:379 |
| `api/catalog/v1/all?onlyAvailableSkus=true` | Store catalog/storefront DTO list. | GCBPKHFJKCE:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<GCBPKHFJKCE+DJGBECJHOKF>> KABHMCDHHIP(System.Boolean INGAKMAAHKL = False):GCBPKHFJKCE.txt:555<br>GCBPKHFJKCE:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<GCBPKHFJKCE+DJGBECJHOKF>> KABHMCDHHIP(System.Boolean INGAKMAAHKL = False):GCBPKHFJKCE.txt:577<br>GCBPKHFJKCE+<>c:System.Void <CancelSubscription>b__41_1():GCBPKHFJKCE_NestedType___c.txt:791 |
| `api/communityboard/` | Community board state DTO. | AAHIIPOCKMB:RecRoom.Async.IPromise`1<AAHIIPOCKMB+JFIKDPALHOL> KIAGEGDDBCG():AAHIIPOCKMB.txt:941 |
| `api/consumables/` | Consumables inventory/list or consume result. | FOEHJBDMMBH:RecRoom.Async.IPromise AKFDEPEHBIN(OOCCCGPICOG BAJGNABECMN, System.Int32 HLHHPEKALPI):FOEHJBDMMBH.txt:1463<br>FOEHJBDMMBH:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<OOCCCGPICOG>> AFMKIHFMMBI(System.Int32 HLHHPEKALPI):FOEHJBDMMBH.txt:1112<br>FOEHJBDMMBH:System.Collections.IEnumerator LCELAOEOIJO(OOCCCGPICOG BAJGNABECMN, System.Int32 FKKAPHAMPMG, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE):FOEHJBDMMBH.txt:838 |
| `api/curatedroomplaylists` | Unknown/route-fragment result; inspect continuation for exact DTO. | FPNPBJBCMKB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<System.Int64>> EPPPMHMMGME():FPNPBJBCMKB.txt:53 |
| `api/objectives/` | Objectives/progress list. | GMPIMLJNACB:System.Collections.IEnumerator IPCNHLDICBM(BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE):GMPIMLJNACB.txt:904<br>GMPIMLJNACB:System.Void MEGMCCFCEIP(System.Int32 CKEBCMEKCJH, BPHGKAEDBPE+OJDJIJDNFHE<MIMANKKMKJG> AFLPGGJMPOE):GMPIMLJNACB.txt:2171<br>GMPIMLJNACB+GIDAGFNPPLC:System.Boolean MoveNext():GMPIMLJNACB_NestedType_GIDAGFNPPLC.txt:568 |
| `api/PlayersBanned/` | Banned-player list or boolean state. | GGLGFENEKBJ:System.Collections.IEnumerator BFJMPGHLGAN(System.Int64 CJFGEMGOJHB, LCCEEFHOBEN+CJFENPHAAHI MEABFEIBEMP, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE):GGLGFENEKBJ.txt:586 |
| `api/relationships/` | Relationship list/state or mutation success. | FGPIDGLCKEF:RecRoom.Async.IPromise`1<LLKHFJDNFMM> CGPBLDOHCEI(System.Int32 CJFGEMGOJHB, CFLMJLFBOKH+MPFKNCFEDHF DIEPOPNJNCO):FGPIDGLCKEF.txt:1454<br>FGPIDGLCKEF:RecRoom.Async.IPromise`1<LLKHFJDNFMM> DLBAPGPACLP(System.Int32 CJFGEMGOJHB):FGPIDGLCKEF.txt:1827<br>FGPIDGLCKEF:RecRoom.Async.IPromise`1<LLKHFJDNFMM> DOIEMGNIMMM(System.Int32 CJFGEMGOJHB, BPHGKAEDBPE+CBEOHBCIPEA AFLPGGJMPOE = null):FGPIDGLCKEF.txt:1197 |
| `api/sanitize/` | Sanitized text/string result. | NKHBKKGOIHL+BBGMJPMOHEO:System.Boolean MoveNext():NKHBKKGOIHL_NestedType_BBGMJPMOHEO.txt:353 |
| `api/storefronts/` | Store catalog/storefront DTO list. | GEAPBDGCKMB:RecRoom.Async.IPromise ACEPPELPFOL(System.Int32 PGGHBNCGLDN, System.Nullable`1<System.Int64> POAOENHPNHE, System.Int32 MNIJHBJDPPA, System.Int32 HLHHPEKALPI):GEAPBDGCKMB.txt:2977<br>GEAPBDGCKMB:RecRoom.Async.IPromise`1<GEAPBDGCKMB+BalanceUpdateResponseDTO`1<GEAPBDGCKMB+RewardBalanceModificationDTO>> LLNACOGDCMF(ACDKILABNNC DLNFAILEHOA, System.Collections.Generic.IEnumerable`1<GEAPBDGCKMB+GrantBalanceRequest> JEDEBGBEGCE):GEAPBDGCKMB.txt:5929<br>GEAPBDGCKMB:RecRoom.Async.IPromise`1<GEAPBDGCKMB+InventionPurchaseResponseDTO> MOANDFACFAP(System.Int64 OEMDIAHHILF, System.Int32 HBIEIIHBDCI, System.String LGHBPPIOOBM):GEAPBDGCKMB.txt:3603 |
| `api/testcasemanagement/` | Test-case management DTO; likely internal/dev only. | AICHDPNIEKI:RecRoom.Async.IPromise CNGKABDOEKH(System.String EPGHLDONDIP, NIDIHGENDJD AKPHCJFIPBB):AICHDPNIEKI.txt:450<br>AICHDPNIEKI:RecRoom.Async.IPromise DHDOAJAGEJN(System.String EPGHLDONDIP):AICHDPNIEKI.txt:360<br>AICHDPNIEKI:RecRoom.Async.IPromise KNOGAGHKIMC(System.String EPGHLDONDIP):AICHDPNIEKI.txt:273 |
| `api/versioncheck/v4?v={0}&p={1}` | Unknown/route-fragment result; inspect continuation for exact DTO. | KMDHPCHFADM:RecRoom.Async.IPromise`1<PCBCGDHBAEL> BPAGDCDBHAM():KMDHPCHFADM.txt:282 |
| `https://apps.apple.com/account/subscriptions` | External URL launch; no RecNet JSON expected. | IOSPlatformManager:RecRoom.Async.IPromise ShowManageSubscriptionPlatformUI():IOSPlatformManager.txt:4865 |
| `https://www.instagram.com/recroom/` | External URL launch; no RecNet JSON expected. | InstagramBulletinBoardUI:System.Void Button_RecRoomOnInstagram():InstagramBulletinBoardUI.txt:1009 |

## player-events

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `api/playerevents/` | Player event DTO/list or mutation success. | GNPDMBPGHBH:RecRoom.Async.IPromise`1<LLNPLLBEJBE> BFBMDMLHHCF(CCMBKDINCAH MFJCKOBPMGA, System.Int64 HNHLJONGKHB, System.Nullable`1<System.Int64> FODGKNJIGOP, System.String MMBOKOLAJFH, System.String LJIGOCDPEJF, System.Collections.Generic.List`1<System.String> CAFPJPHILMN, System.String HFLPBHHAFIO, System.DateTime JODPOANPJNK, System.DateTime BCANLCHBKJE, CMCAFKLAHCD PONCIIJOHIE, System.Nullable`1<System.Int64> AOMBLGBCENO):GNPDMBPGHBH.txt:4280<br>GNPDMBPGHBH:RecRoom.Async.IPromise`1<LLNPLLBEJBE> EKEOHKINPDK(CCMBKDINCAH MFJCKOBPMGA):GNPDMBPGHBH.txt:6151<br>GNPDMBPGHBH:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<PBIJAPEOEDO>> MBHCOIGLAIF(System.Int64 MOKMJAMNEFP, System.Boolean INGAKMAAHKL = False):GNPDMBPGHBH.txt:3736 |
| `api/playerevents/v1/all` | Paged event list or filter/tag list. | GNPDMBPGHBH:RecRoom.Async.IPromise`1<FNBMIJGOOJM> JKFDNKOBOJE():GNPDMBPGHBH.txt:5504 |
| `api/playerevents/v1/bulkInvite` | Bulk invite success/failure result. | GNPDMBPGHBH:RecRoom.Async.IPromise`1<PDOBNLOLBAF> MGDLGLLOEMG(System.Int64 MOKMJAMNEFP, System.Collections.Generic.List`1<System.Int32> MHCONOPOOKJ):GNPDMBPGHBH.txt:5013 |
| `api/playerevents/v1/deleteResponse` | Primitive success or report/delete result. | GNPDMBPGHBH:RecRoom.Async.IPromise`1<DGOPHENCPOC> FGFDJIKJKNN(System.Int64 MOKMJAMNEFP, FFEICMIIBMC DHLDCHCKBPC):GNPDMBPGHBH.txt:4769 |
| `api/playerevents/v1/report` | Primitive success or report/delete result. | GNPDMBPGHBH:RecRoom.Async.IPromise`1<KLAMKCBENEA> GLHAJLLNPBJ(System.Int64 MOKMJAMNEFP, LCCEEFHOBEN+CJFENPHAAHI MEABFEIBEMP, System.String EFDBFLPKHKA):GNPDMBPGHBH.txt:5939 |
| `api/playerevents/v1/respond` | Player event DTO/list or mutation success. | GNPDMBPGHBH:RecRoom.Async.IPromise`1<DGOPHENCPOC> EEJGIOKDBFI(System.Int64 MOKMJAMNEFP, FFEICMIIBMC DHLDCHCKBPC):GNPDMBPGHBH.txt:4567 |
| `api/playerevents/v1/search?` | Paged event list or filter/tag list. | GNPDMBPGHBH:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CCMBKDINCAH>> ADMFNGBFPIE(System.String CNBKKCJAHPP, GNPDMBPGHBH+EHKGCEONBIB DKLJMLFNEEN = 0, System.Nullable`1<GNPDMBPGHBH+OPHEGMJHPAD> LBFLCMJFDPC = null):GNPDMBPGHBH.txt:9248 |
| `api/playerevents/v1/searchlive?` | Paged event list or filter/tag list. | GNPDMBPGHBH:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<NMKMJKIHDNE>> JBOMFDKLBOI(System.String CNBKKCJAHPP):GNPDMBPGHBH.txt:8935 |
| `api/playerevents/v1/tagfilters` | Paged event list or filter/tag list. | GNPDMBPGHBH:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<System.String>> LIPBAJOAIJC(GNPDMBPGHBH+FFCCJHEPNDK KINAEJLCDEG):GNPDMBPGHBH.txt:9697 |
| `api/playerevents/v2` | Player event DTO/list or mutation success. | GNPDMBPGHBH:RecRoom.Async.IPromise`1<LLNPLLBEJBE> BBPEHLACMLH(System.Int64 HNHLJONGKHB, System.Nullable`1<System.Int64> FODGKNJIGOP, System.Nullable`1<System.Int64> AOMBLGBCENO, System.String MMBOKOLAJFH, System.String LJIGOCDPEJF, System.Collections.Generic.List`1<System.String> CAFPJPHILMN, System.String HFLPBHHAFIO, System.DateTime JODPOANPJNK, System.DateTime BCANLCHBKJE, CMCAFKLAHCD PONCIIJOHIE):GNPDMBPGHBH.txt:4067 |

## players

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `/api/playerReputation/v1/{0}` | Player reputation DTO or bulk map/list with moderation/reputation state. | EBAIPNMBKLK:RecRoom.Async.IPromise`1<JGEBJEJLNBK> JNLDMDCGHHJ(System.Int32 GKLPIFBPGOD):EBAIPNMBKLK.txt:1231 |
| `/api/playerReputation/v1/bulk` | Player reputation DTO or bulk map/list with moderation/reputation state. | EBAIPNMBKLK:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<JGEBJEJLNBK>> KGPDFJMKJKL(System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG):EBAIPNMBKLK.txt:1631 |
| `/api/players/v1/progression/{0}` | Player progression DTO or bulk map/list with XP, level, currencies/progress fields. | ACJLCBNBJDK:RecRoom.Async.IPromise`1<NFMBDLEJEDD> AAGPDEDFCNI(System.Int32 GKLPIFBPGOD):ACJLCBNBJDK.txt:1673 |
| `/api/players/v1/progression/bulk` | Player progression DTO or bulk map/list with XP, level, currencies/progress fields. | ACJLCBNBJDK:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<NFMBDLEJEDD>> MCFMHADBDBI(System.Collections.Generic.List`1<System.Int32> ILNGMAANNDG):ACJLCBNBJDK.txt:2051 |
| `api/players/v2/objectives` | Objective/progression list for the current player. | ACJLCBNBJDK:System.Void EIFPPIPOIHB(System.Collections.Generic.List`1<ProgressionManager+PIHHFPDKFAG> DBDFCMJFNEC):ACJLCBNBJDK.txt:816 |

## playlists

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `featuredrooms/current` | Paged list of playlist or mixed room-playlist summaries. | EJDCNGBEICB:RecRoom.Async.IPromise`1<NMPFCIJPODA> CFKDADKHAGB():EJDCNGBEICB.txt:2687 |
| `playlists/{0}` | Playlist detail DTO or list. | EJDCNGBEICB:RecRoom.Async.IPromise OFACCDEDIHE(System.Int64 DDMPFMPILCE):EJDCNGBEICB.txt:6619<br>EJDCNGBEICB:RecRoom.Async.IPromise`1<BMFAGMFKODA> POOBMHAAMLJ(System.Int64 DDMPFMPILCE):EJDCNGBEICB.txt:2192<br>EJDCNGBEICB:RecRoom.Async.IPromise`1<KMKPEOGJDFK> PDLNHHAPPIP(System.Int64 DDMPFMPILCE):EJDCNGBEICB.txt:1659 |
| `playlists/{0}/accessibility` | Mutation success result with updated playlist detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<BMFAGMFKODA> KLHJFKPFPIC(System.Int64 DDMPFMPILCE, DPLPMKMFMPB PONCIIJOHIE):EJDCNGBEICB.txt:7323 |
| `playlists/{0}/description` | Mutation success result with updated playlist detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<BMFAGMFKODA> EPJMNNPLNDM(System.Int64 DDMPFMPILCE, System.String LJIGOCDPEJF):EJDCNGBEICB.txt:6839 |
| `playlists/{0}/image` | Mutation success result with updated playlist detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<BMFAGMFKODA> CKMILKDKDCN(System.Int64 DDMPFMPILCE, System.String HFLPBHHAFIO):EJDCNGBEICB.txt:6955 |
| `playlists/{0}/interactionby/me` | Playlist detail DTO or list. | EJDCNGBEICB:RecRoom.Async.IPromise`1<CJODCLDGFCF> AINDJCIMJOB(System.Int64 DDMPFMPILCE):EJDCNGBEICB.txt:8600 |
| `playlists/{0}/interactionby/me/cheer` | Playlist detail DTO or list. | EJDCNGBEICB:RecRoom.Async.IPromise CIPEENNIAFL(System.Int64 DDMPFMPILCE):EJDCNGBEICB.txt:8733<br>EJDCNGBEICB:RecRoom.Async.IPromise LONLMONNPGL(System.Int64 DDMPFMPILCE):EJDCNGBEICB.txt:8665 |
| `playlists/{0}/interactionby/me/favorite` | Playlist detail DTO or list. | EJDCNGBEICB:RecRoom.Async.IPromise GDELFOHOPGF(System.Int64 DDMPFMPILCE):EJDCNGBEICB.txt:8801<br>EJDCNGBEICB:RecRoom.Async.IPromise NLNKEGDKFCG(System.Int64 DDMPFMPILCE):EJDCNGBEICB.txt:8869 |
| `playlists/{0}/levelvoting` | Mutation success result with updated playlist detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<BMFAGMFKODA> PPBLDOPNJKC(System.Int64 DDMPFMPILCE, System.Boolean NNBHCDIKILH):EJDCNGBEICB.txt:7582 |
| `playlists/{0}/name` | Mutation success result with updated playlist detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<BMFAGMFKODA> NINPMBNNHMG(System.Int64 DDMPFMPILCE, System.String MMBOKOLAJFH):EJDCNGBEICB.txt:6723 |
| `playlists/{0}/restrictions` | Mutation success result with updated playlist detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<BMFAGMFKODA> LDLJKJLNLEE(System.Int64 DDMPFMPILCE, System.Boolean DDMOEIAHJBK, System.Boolean FOBLJIBLCNI, System.Boolean GELHFEFGJLA, System.Boolean NFIGHDPKLJF):EJDCNGBEICB.txt:7454 |
| `playlists/{0}/rooms/{1}` | Success result for adding/removing/reordering room in playlist. | EJDCNGBEICB:RecRoom.Async.IPromise`1<BMFAGMFKODA> JBMCPFHGIMD(System.Int64 DDMPFMPILCE, System.Int64 HNHLJONGKHB):EJDCNGBEICB.txt:7765<br>EJDCNGBEICB:RecRoom.Async.IPromise`1<BMFAGMFKODA> NLOEBFMNCFM(System.Int64 DDMPFMPILCE, System.Int64 HNHLJONGKHB):EJDCNGBEICB.txt:7679 |
| `playlists/{0}/tags` | Mutation success result with updated playlist detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<BMFAGMFKODA> ALCFNJJHDMG(System.Int64 DDMPFMPILCE, System.Collections.Generic.IReadOnlyList`1<System.String> LBAOCHFCLPO, System.Collections.Generic.IReadOnlyList`1<System.String> CAFPJPHILMN):EJDCNGBEICB.txt:7080 |
| `playlists/{0}/warning` | Mutation success result with updated playlist detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<BMFAGMFKODA> KEADPPJCJGG(System.Int64 DDMPFMPILCE, GPDIAKNEBKH LEFPAAGGFFA, System.String NLEBEKDHJIJ):EJDCNGBEICB.txt:7206 |
| `playlists/bulk` | Array/list of playlist detail DTOs. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KMKPEOGJDFK>> KHMJIKNHJHP(System.Collections.Generic.IReadOnlyList`1<System.Int64> ANIKDNFLDIG):EJDCNGBEICB.txt:1896<br>EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KMKPEOGJDFK>> KHMJIKNHJHP(System.Collections.Generic.IReadOnlyList`1<System.String> CJMAAFFAKDO):EJDCNGBEICB.txt:2068 |
| `playlists/cheeredby/me` | Paged list of playlist or mixed room-playlist summaries. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KMKPEOGJDFK>> EFFMOEGALDB():EJDCNGBEICB.txt:2400 |
| `playlists/createdby/me` | Paged list of playlist or mixed room-playlist summaries. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KMKPEOGJDFK>> PLPMPFDOICH():EJDCNGBEICB.txt:2361 |
| `playlists/favoritedby/me` | Paged list of playlist or mixed room-playlist summaries. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KMKPEOGJDFK>> FAMGMKHMCIN():EJDCNGBEICB.txt:2439 |
| `playlists/visitedby/me` | Paged list of playlist or mixed room-playlist summaries. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KMKPEOGJDFK>> OMMENACPKGH():EJDCNGBEICB.txt:2478 |
| `roomsandplaylists/hot` | Paged list of playlist or mixed room-playlist summaries. | EJDCNGBEICB:RecRoom.Async.IPromise`1<HJKAOMOICJG> EBBHGHEGBKG(System.Collections.Generic.IReadOnlyList`1<System.String> CAFPJPHILMN):EJDCNGBEICB.txt:2645 |
| `roomsandplaylists/search` | Paged list of playlist or mixed room-playlist summaries. | EJDCNGBEICB:RecRoom.Async.IPromise`1<HJKAOMOICJG> HFHDFLDCIEM(System.String CNBKKCJAHPP):EJDCNGBEICB.txt:2560 |

## quickplay

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `api/quickPlay/` | Quick-play room/activity selection DTO. | ADOIEPDDEBO:RecRoom.Async.IPromise CPGECIJBBKF():ADOIEPDDEBO.txt:358 |

## reporting

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `{0}/api/userreporting` | Report submission or Unity user-reporting success DTO. | KHGPLGBHIAH:System.Void PPOCPFGCALB(LGFHFDMDJKK GIGKODMKKHJ, System.Action`2<System.Single, System.Single> DFPGDEOCNIH, System.Action`2<System.Boolean, LGFHFDMDJKK> AFLPGGJMPOE):KHGPLGBHIAH.txt:2838 |
| `api/banappeal/generateCode` | Generated ban appeal code DTO/string. | LCCEEFHOBEN:RecRoom.Async.IPromise`1<System.String> LONDNAJPCEE():LCCEEFHOBEN.txt:2149 |
| `api/PlayerReporting/` | Report submission or Unity user-reporting success DTO. | LCCEEFHOBEN:RecRoom.Async.IPromise FCPPPBKJFGK(LCCEEFHOBEN+OAODPJFGPAF GEDCEIDOKJL, System.String NGPMADFHHKP, System.Nullable`1<System.Int32> IMHODAEGGON = null):LCCEEFHOBEN.txt:5148<br>LCCEEFHOBEN:RecRoom.Async.IPromise IEAOIIFNOGN(System.String PLDIBPNDJIO):LCCEEFHOBEN.txt:4715<br>LCCEEFHOBEN:RecRoom.Async.IPromise`1<HOGFDJNNMHM> NEFPDKLMDHK():LCCEEFHOBEN.txt:895 |
| `api/PlayerReporting/v1/voteToKickReasons` | List of vote-to-kick/player-report reason DTOs. | LCCEEFHOBEN:RecRoom.Async.IPromise HHBLGNHOGLN():LCCEEFHOBEN.txt:2417 |
| `https://userreporting.cloud.unity3d.com/api/userreporting/projects/{0}/ping` | Report submission or Unity user-reporting success DTO. | UserReportingScript:System.Void Start():UserReportingScript.txt:1174 |

## room-keys

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `api/roomkeys/` | Room key DTO/list or success result for create/update/delete/mine flows. | IOIBJBLIBKM:RecRoom.Async.IPromise`1<BMHHFIGBOFD> ACCJPCMPJMH(System.Int64 BGICHOOBKLD):IOIBJBLIBKM.txt:1299<br>IOIBJBLIBKM:RecRoom.Async.IPromise`1<System.Boolean> HECNNKLKKAG(System.Int64 BGICHOOBKLD):IOIBJBLIBKM.txt:1517<br>IOIBJBLIBKM:RecRoom.Async.IPromise`1<System.Boolean> LAPDFFOJCNP(System.Int32 CJFGEMGOJHB, AMAGKLLBGEC AAHIIDDIBFD):IOIBJBLIBKM.txt:1866 |
| `api/roomkeys/v1/create` | Room key DTO/list or success result for create/update/delete/mine flows. | IOIBJBLIBKM:RecRoom.Async.IPromise`1<BCKIBFNPIPD> BIAKPHAECJC(System.String MMBOKOLAJFH, System.String LJIGOCDPEJF, System.Int32 MACNIENMFHJ):IOIBJBLIBKM.txt:573 |
| `api/roomkeys/v1/mine` | Room key DTO/list or success result for create/update/delete/mine flows. | IOIBJBLIBKM:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<AMAGKLLBGEC>> IGFLECMACBE():IOIBJBLIBKM.txt:1704 |
| `api/roomkeys/v1/update` | Room key DTO/list or success result for create/update/delete/mine flows. | IOIBJBLIBKM:RecRoom.Async.IPromise`1<BCKIBFNPIPD> MLFKLGGPLNM(System.Int64 BGICHOOBKLD, System.String MMBOKOLAJFH = null, System.String LJIGOCDPEJF = null, System.Nullable`1<System.Int32> MACNIENMFHJ = null):IOIBJBLIBKM.txt:1008 |

## rooms

| Request literal / fragment | Expected result shape | Evidence |
| --- | --- | --- |
| `api/rooms/v1/filters` | Report/permission/filter DTO or primitive success/boolean. | OJMCBOKJFOF+<>c:RecRoom.Async.IPromise`1<AEBEPCMAABC> <GetFilters>b__114_0():OJMCBOKJFOF_NestedType___c.txt:1617 |
| `api/rooms/v1/verifyRole` | Report/permission/filter DTO or primitive success/boolean. | OJMCBOKJFOF:RecRoom.Async.IPromise INDDAAGNMHF(System.String LHOMKMINCHH):OJMCBOKJFOF.txt:12963 |
| `api/rooms/v2/report` | Report/permission/filter DTO or primitive success/boolean. | OJMCBOKJFOF:RecRoom.Async.IPromise GECDGBACBFP(System.Int64 HNHLJONGKHB, LCCEEFHOBEN+CJFENPHAAHI MEABFEIBEMP, System.String EFDBFLPKHKA):OJMCBOKJFOF.txt:12627 |
| `hot_rooms/` | Paged array/list of room summaries/details. | OJMCBOKJFOF:RecRoom.Async.IPromise`1<System.Collections.Generic.IReadOnlyList`1<KLCOGEIGEBJ>> DLPLPKCNLNA(System.Collections.Generic.IReadOnlyList`1<System.String> CAFPJPHILMN):OJMCBOKJFOF.txt:3238<br>OJMCBOKJFOF+PEBCIEAPGON:System.String CMOIGEDECGM(System.Collections.Generic.IEnumerable`1<System.String> CAFPJPHILMN):OJMCBOKJFOF_NestedType_PEBCIEAPGON.txt:39 |
| `hot_roomsandplaylists/` | Paged array/list of room summaries/details. | OJMCBOKJFOF:RecRoom.Async.IPromise`1<System.Collections.Generic.IReadOnlyList`1<MKAMHOIHOJK>> EBBHGHEGBKG(System.Collections.Generic.IReadOnlyList`1<System.String> CAFPJPHILMN):OJMCBOKJFOF.txt:4743<br>OJMCBOKJFOF+PEBCIEAPGON:System.String MMCLGMMDDJF(System.Collections.Generic.IEnumerable`1<System.String> CAFPJPHILMN):OJMCBOKJFOF_NestedType_PEBCIEAPGON.txt:121 |
| `rooms/{0}` | Room detail DTO, room summary list, or mutation success depending on verb. | EJDCNGBEICB:RecRoom.Async.IPromise KHDPHIGPEEH(System.Int64 HNHLJONGKHB):EJDCNGBEICB.txt:2923<br>EJDCNGBEICB:RecRoom.Async.IPromise`1<KLCOGEIGEBJ> NHBPIIGDAJP(System.Int64 HNHLJONGKHB):EJDCNGBEICB.txt:82<br>EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> CJKHNIIJFIN(System.Int64 HNHLJONGKHB):EJDCNGBEICB.txt:615 |
| `rooms/{0}/accessibility` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> BPDNAMEDJEG(System.Int64 HNHLJONGKHB, DPLPMKMFMPB PONCIIJOHIE):EJDCNGBEICB.txt:3627 |
| `rooms/{0}/automute` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> CPFCPEONBGC(System.Int64 HNHLJONGKHB, System.Boolean AMJMLANOGKE):EJDCNGBEICB.txt:4014 |
| `rooms/{0}/bans` | Room ban list or success result for ban/import/unban mutation. | EJDCNGBEICB:RecRoom.Async.IPromise CCGIGHDHNLM(System.Int64 HNHLJONGKHB, System.Collections.Generic.IReadOnlyList`1<System.Int32> ILNGMAANNDG, KKIHLHLNGCK BFNPCCFPJHP):EJDCNGBEICB.txt:7939<br>EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<EAENIFLCDGI>> CGIPIOMBAFM(System.Int64 HNHLJONGKHB):EJDCNGBEICB.txt:7832 |
| `rooms/{0}/bans/{1}` | Room ban list or success result for ban/import/unban mutation. | EJDCNGBEICB:RecRoom.Async.IPromise JMCJDGIMKPK(System.Int64 HNHLJONGKHB, System.Int32 GKLPIFBPGOD, KKIHLHLNGCK BFNPCCFPJHP):EJDCNGBEICB.txt:8186 |
| `rooms/{0}/bans/import` | Room ban list or success result for ban/import/unban mutation. | EJDCNGBEICB:RecRoom.Async.IPromise PFEEJDKPPJI(System.Int64 HNHLJONGKHB, System.Int64 MOKIOEJBMFC):EJDCNGBEICB.txt:8054 |
| `rooms/{0}/clone` | New cloned room/subroom detail DTO. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> OFFECAMHJGE(System.Int64 HNHLJONGKHB, System.String MMBOKOLAJFH):EJDCNGBEICB.txt:2843 |
| `rooms/{0}/cloning` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> LJMHBJKDJCE(System.Int64 HNHLJONGKHB, System.Boolean GPAGMKFIKNG):EJDCNGBEICB.txt:3886 |
| `rooms/{0}/comments` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> FKADIOPHOGC(System.Int64 HNHLJONGKHB, System.Boolean IMHMMPKLGEP):EJDCNGBEICB.txt:4142 |
| `rooms/{0}/description` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> CFNFNEJGCEF(System.Int64 HNHLJONGKHB, System.String LJIGOCDPEJF):EJDCNGBEICB.txt:3143 |
| `rooms/{0}/image` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> OGFLGMMNMDD(System.Int64 HNHLJONGKHB, System.String HFLPBHHAFIO):EJDCNGBEICB.txt:3259 |
| `rooms/{0}/interactionby/me` | Room detail DTO, room summary list, or mutation success depending on verb. | EJDCNGBEICB:RecRoom.Async.IPromise`1<CJODCLDGFCF> CKJBGHEIGBI(System.Int64 HNHLJONGKHB):EJDCNGBEICB.txt:8266 |
| `rooms/{0}/interactionby/me/cheer` | Room detail DTO, room summary list, or mutation success depending on verb. | EJDCNGBEICB:RecRoom.Async.IPromise JFBCCIHBKPP(System.Int64 HNHLJONGKHB):EJDCNGBEICB.txt:8399<br>EJDCNGBEICB:RecRoom.Async.IPromise PEKMAJPMCDE(System.Int64 HNHLJONGKHB):EJDCNGBEICB.txt:8331 |
| `rooms/{0}/interactionby/me/favorite` | Room detail DTO, room summary list, or mutation success depending on verb. | EJDCNGBEICB:RecRoom.Async.IPromise DIFNOPBMBFO(System.Int64 HNHLJONGKHB):EJDCNGBEICB.txt:8467<br>EJDCNGBEICB:RecRoom.Async.IPromise OGBLBGALMDC(System.Int64 HNHLJONGKHB):EJDCNGBEICB.txt:8535 |
| `rooms/{0}/modify` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> ONBIHBFOJKD(System.Int64 HNHLJONGKHB, System.String MMBOKOLAJFH, System.String LJIGOCDPEJF, DPLPMKMFMPB PONCIIJOHIE, System.Boolean DDMOEIAHJBK, System.Boolean FOBLJIBLCNI, System.Boolean GELHFEFGJLA, System.Boolean NFIGHDPKLJF, System.Boolean GPAGMKFIKNG, System.Boolean AMJMLANOGKE, System.Boolean IMHMMPKLGEP, System.Boolean NFJINCPDPDG):EJDCNGBEICB.txt:6298 |
| `rooms/{0}/name` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> DNOGGKKHMHI(System.Int64 HNHLJONGKHB, System.String MMBOKOLAJFH):EJDCNGBEICB.txt:3027 |
| `rooms/{0}/promo_external` | Promo image/external-link list or mutation success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> FBNHEKCLMCJ(System.Int64 HNHLJONGKHB, NCELIDGFOEM GEDCEIDOKJL, System.String EKGHGPLFMPJ):EJDCNGBEICB.txt:4862 |
| `rooms/{0}/promo_external/{1}/{2}` | Promo image/external-link list or mutation success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> ACGJJCMFOCH(System.Int64 HNHLJONGKHB, NCELIDGFOEM GEDCEIDOKJL, System.String EKGHGPLFMPJ):EJDCNGBEICB.txt:4970 |
| `rooms/{0}/promo_images` | Promo image/external-link list or mutation success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> CFIKOAAOHFH(System.Int64 HNHLJONGKHB, System.String HFLPBHHAFIO):EJDCNGBEICB.txt:4663 |
| `rooms/{0}/promo_images/{1}` | Promo image/external-link list or mutation success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> OCCGHPAHGKC(System.Int64 HNHLJONGKHB, System.String HFLPBHHAFIO):EJDCNGBEICB.txt:4749 |
| `rooms/{0}/restrictions` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> ONEJODPFOMP(System.Int64 HNHLJONGKHB, System.Boolean DDMOEIAHJBK, System.Boolean FOBLJIBLCNI, System.Boolean GELHFEFGJLA, System.Boolean NFIGHDPKLJF):EJDCNGBEICB.txt:3758 |
| `rooms/{0}/roles` | Room role list/detail or success result for role mutation/invite. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<CGCEKBCIHJC>> MIGOLOHAIAL(System.Int64 HNHLJONGKHB):EJDCNGBEICB.txt:1254 |
| `rooms/{0}/roles/{1}` | Room role list/detail or success result for role mutation/invite. | EJDCNGBEICB:RecRoom.Async.IPromise`1<CGCEKBCIHJC> EKOKFNHHMPI(System.Int64 HNHLJONGKHB, System.Int32 GKLPIFBPGOD):EJDCNGBEICB.txt:1332<br>EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> GJMGBBCIHGH(System.Int64 HNHLJONGKHB, System.Int32 GKLPIFBPGOD, LMLJHMJEIGM IENKDAKBEDP):EJDCNGBEICB.txt:4405 |
| `rooms/{0}/roles/{1}/invite` | Room role list/detail or success result for role mutation/invite. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> MJOHJLHDMHB(System.Int64 HNHLJONGKHB, System.Int32 GKLPIFBPGOD, LMLJHMJEIGM IENKDAKBEDP):EJDCNGBEICB.txt:4543 |
| `rooms/{0}/subrooms` | Room detail DTO, room summary list, or mutation success depending on verb. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> OGNIELCNHIM(System.Int64 HNHLJONGKHB, System.String MMBOKOLAJFH):EJDCNGBEICB.txt:5487 |
| `rooms/{0}/subrooms/{1}` | Room detail DTO, room summary list, or mutation success depending on verb. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> GGCOGNAGPBP(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP):EJDCNGBEICB.txt:5809 |
| `rooms/{0}/subrooms/{1}/accessibility` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> POGEHAMJBPE(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, DPLPMKMFMPB PONCIIJOHIE):EJDCNGBEICB.txt:5369 |
| `rooms/{0}/subrooms/{1}/clone` | New cloned room/subroom detail DTO. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> OGEKCICPACB(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP):EJDCNGBEICB.txt:5583 |
| `rooms/{0}/subrooms/{1}/data` | Room/subroom data descriptor pointing to binary room blob; blob itself must be bytes from CDN/storage. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> HOPMJFDKPDL(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, System.String MEIIMAIGBJD, System.Collections.Generic.Dictionary`2<System.Int64, System.Int32> PNOBBNPJDLI, System.Int32 OMCLBIFCHLF):EJDCNGBEICB.txt:5995 |
| `rooms/{0}/subrooms/{1}/datahistory` | List/page of saved room data blob history entries. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<DHOCPFIOKHD>> HAMBEABEIOC(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP):EJDCNGBEICB.txt:1190 |
| `rooms/{0}/subrooms/{1}/maxplayers` | Room detail DTO, room summary list, or mutation success depending on verb. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> AEBGMLMMKOJ(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, System.Int32 FFAGIDMFPIF):EJDCNGBEICB.txt:5235 |
| `rooms/{0}/subrooms/{1}/modify` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> JGMMJMHLHBO(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, System.String MMBOKOLAJFH, DPLPMKMFMPB PONCIIJOHIE, System.Int32 FFAGIDMFPIF):EJDCNGBEICB.txt:6443 |
| `rooms/{0}/subrooms/{1}/move` | Room detail DTO, room summary list, or mutation success depending on verb. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> OHHAFAIEFHP(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, System.Nullable`1<System.Int64> PAJMLPFMBEJ):EJDCNGBEICB.txt:5709 |
| `rooms/{0}/subrooms/{1}/name` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> PAENPJCDLNJ(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, System.String MMBOKOLAJFH):EJDCNGBEICB.txt:5099 |
| `rooms/{0}/subrooms/{1}/restoredata` | Success result and refreshed room data descriptor/current blob pointer. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> BBGBOCGJFOP(System.Int64 HNHLJONGKHB, System.Int64 FODGKNJIGOP, System.String MEIIMAIGBJD):EJDCNGBEICB.txt:6133 |
| `rooms/{0}/tags` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> ENGFFJGIEGH(System.Int64 HNHLJONGKHB, System.Collections.Generic.IReadOnlyList`1<System.String> LBAOCHFCLPO, System.Collections.Generic.IReadOnlyList`1<System.String> CAFPJPHILMN):EJDCNGBEICB.txt:3384 |
| `rooms/{0}/voice_chat_encryption` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> PKDCMMFILPB(System.Int64 HNHLJONGKHB, System.Boolean NFJINCPDPDG):EJDCNGBEICB.txt:4270 |
| `rooms/{0}/warning` | Mutation success result, usually with updated room detail or primitive success. | EJDCNGBEICB:RecRoom.Async.IPromise`1<PPENFJMFPNE> NPACFDBCHDP(System.Int64 HNHLJONGKHB, GPDIAKNEBKH LEFPAAGGFFA, System.String NLEBEKDHJIJ):EJDCNGBEICB.txt:3510 |
| `rooms/base` | Room detail DTO, room summary list, or mutation success depending on verb. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> LMEPPFGPHLA():EJDCNGBEICB.txt:784 |
| `rooms/bulk` | Array/list of room detail DTOs for requested room ids. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> JPNBCKICJIH(System.Collections.Generic.IReadOnlyList`1<System.Int64> HJLOFEGEMHE):EJDCNGBEICB.txt:319<br>EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> JPNBCKICJIH(System.Collections.Generic.IReadOnlyList`1<System.String> GDDBDIJIBCO):EJDCNGBEICB.txt:491 |
| `rooms/cheeredby/me` | Paged array/list of room summaries/details. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> GBMMJLHIDOK():EJDCNGBEICB.txt:940 |
| `rooms/createdby/{0}` | Paged array/list of room summaries/details. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> OGIHJJDNPKJ(System.Int32 GKLPIFBPGOD):EJDCNGBEICB.txt:1073 |
| `rooms/createdby/me` | Paged array/list of room summaries/details. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> MIMNJGKEGMF():EJDCNGBEICB.txt:823 |
| `rooms/favoritedby/me` | Paged array/list of room summaries/details. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> AFOLHKGPMOC():EJDCNGBEICB.txt:979 |
| `rooms/hot` | Paged array/list of room summaries/details. | EJDCNGBEICB:RecRoom.Async.IPromise`1<IDLBPALJJDJ> DLPLPKCNLNA(System.Collections.Generic.IReadOnlyList`1<System.String> CAFPJPHILMN):EJDCNGBEICB.txt:1601 |
| `rooms/moderatedby/me` | Paged array/list of room summaries/details. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> IMKAAIEFGIG():EJDCNGBEICB.txt:901 |
| `rooms/ownedby/me` | Paged array/list of room summaries/details. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> FNMNFLFIBJG():EJDCNGBEICB.txt:862 |
| `rooms/recommendations` | Paged array/list of room summaries/details. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<ANNKHNFLMNP>> JDKLBNBIEAH(AMBEPDPIBPA NLNJMJGOKFI, System.Int16 NJJLKKLALND):EJDCNGBEICB.txt:1430 |
| `rooms/rro_ids` | Room detail DTO, room summary list, or mutation success depending on verb. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<System.Int64>> JDMBPCJDEMF():EJDCNGBEICB.txt:1119 |
| `rooms/search` | Paged array/list of room summaries/details. | EJDCNGBEICB:RecRoom.Async.IPromise`1<IDLBPALJJDJ> IAAKGMOGLFN(System.String CNBKKCJAHPP):EJDCNGBEICB.txt:1516 |
| `rooms/visitedby/me` | Paged array/list of room summaries/details. | EJDCNGBEICB:RecRoom.Async.IPromise`1<System.Collections.Generic.List`1<KLCOGEIGEBJ>> INKFBOEEKDA():EJDCNGBEICB.txt:1018 |
| `search_rooms/` | Paged array/list of room summaries/details. | OJMCBOKJFOF:RecRoom.Async.IPromise`1<System.Collections.Generic.IReadOnlyList`1<KLCOGEIGEBJ>> IAAKGMOGLFN(System.String CNBKKCJAHPP):OJMCBOKJFOF.txt:3064<br>OJMCBOKJFOF+PEBCIEAPGON:System.String IAAKGMOGLFN(System.String CNBKKCJAHPP):OJMCBOKJFOF_NestedType_PEBCIEAPGON.txt:76 |
| `search_roomsandplaylists/` | Paged array/list of room summaries/details. | OJMCBOKJFOF:RecRoom.Async.IPromise`1<System.Collections.Generic.IReadOnlyList`1<MKAMHOIHOJK>> HFHDFLDCIEM(System.String CNBKKCJAHPP):OJMCBOKJFOF.txt:4567<br>OJMCBOKJFOF+PEBCIEAPGON:System.String HFHDFLDCIEM(System.String CNBKKCJAHPP):OJMCBOKJFOF_NestedType_PEBCIEAPGON.txt:158 |

## March 2020 Literal Appendix

The March archive exposes request evidence mostly through `Il2CppDump/stringliteral.json`, so this appendix lists request-like literals without method-body context. Expected shapes follow the same rules as the December catalog.

CSV: `docs/recroom-2020-03-request-literals.csv`

| Family | Literal | Expected result shape |
| --- | --- | --- |
| bootstrap-account | `.rec.net` | Name-server/bootstrap URL, RecNet host suffix, or account recovery page; name-server returns service base URL object. |
| bootstrap-account | `https://ns.rec.net/?v=2` | Name-server/bootstrap URL, RecNet host suffix, or account recovery page; name-server returns service base URL object. |
| bootstrap-account | `https://rec.net` | Name-server/bootstrap URL, RecNet host suffix, or account recovery page; name-server returns service base URL object. |
| bootstrap-account | `https://rec.net/password/recover` | Name-server/bootstrap URL, RecNet host suffix, or account recovery page; name-server returns service base URL object. |
| config | `/config/{0}` | Config object for requested key. |
| config | `/configuration/system.runtime.remoting` | Config object for requested key. |
| config | `api/config/` | Config object for requested key. |
| inventions | `api/inventions/` | Invention detail/version/save/publish/download DTO or mutation success. |
| inventions | `api/inventions/v3/addversion` | Invention detail/version/save/publish/download DTO or mutation success. |
| inventions | `api/inventions/v3/save` | Invention detail/version/save/publish/download DTO or mutation success. |
| misc | `{0}v1/bulkignoreplatformusers` | Route literal needing call-site confirmation. |
| misc | `{0}v1/bulkInvite` | Route literal needing call-site confirmation. |
| misc | `{0}v1/consume` | Route literal needing call-site confirmation. |
| misc | `{0}v2/gifts/consume/` | Route literal needing call-site confirmation. |
| misc | `/room/` | Route literal needing call-site confirmation. |
| misc | `/room/{0}` | Route literal needing call-site confirmation. |
| misc | `account/{0}` | Route literal needing call-site confirmation. |
| misc | `account/{0}/bio` | Route literal needing call-site confirmation. |
| misc | `account/bulk` | Route literal needing call-site confirmation. |
| misc | `account/bulk?` | Route literal needing call-site confirmation. |
| misc | `account/bulk/` | Route literal needing call-site confirmation. |
| misc | `account/create` | Route literal needing call-site confirmation. |
| misc | `account/me` | Route literal needing call-site confirmation. |
| misc | `account/me/` | Route literal needing call-site confirmation. |
| misc | `account/me/changepassword` | Route literal needing call-site confirmation. |
| misc | `account/me/haspassword` | Route literal needing call-site confirmation. |
| misc | `account/recoverpassword` | Route literal needing call-site confirmation. |
| misc | `account/search?name=` | Route literal needing call-site confirmation. |
| misc | `Activities/Dormroom/Scenes/holoHelperRecordingStudio` | Route literal needing call-site confirmation. |
| misc | `api/activities/charades/v1/words` | Route literal needing call-site confirmation. |
| misc | `api/announcement/v1/get` | Route literal needing call-site confirmation. |
| misc | `api/avatar/` | Route literal needing call-site confirmation. |
| misc | `api/bugreporting/` | Route literal needing call-site confirmation. |
| misc | `api/catalog/v1/all?onlyAvailableSkus=true` | Route literal needing call-site confirmation. |
| misc | `api/challenge/` | Route literal needing call-site confirmation. |
| misc | `api/checklist/` | Route literal needing call-site confirmation. |
| misc | `api/communityboard/` | Route literal needing call-site confirmation. |
| misc | `api/consumables/` | Route literal needing call-site confirmation. |
| misc | `api/equipment/` | Route literal needing call-site confirmation. |
| misc | `api/gameconfigs/` | Route literal needing call-site confirmation. |
| misc | `api/groups/` | Route literal needing call-site confirmation. |
| misc | `api/images/` | Route literal needing call-site confirmation. |
| misc | `api/messages/` | Route literal needing call-site confirmation. |
| misc | `api/messages/v1/IOSClearDeviceToken` | Route literal needing call-site confirmation. |
| misc | `api/messages/v1/IOSModifyNotificationPreferences` | Route literal needing call-site confirmation. |
| misc | `api/messages/v1/IOSResetNotificationPreferencesBadgeCount` | Route literal needing call-site confirmation. |
| misc | `api/messages/v1/IOSSaveDeviceToken` | Route literal needing call-site confirmation. |
| misc | `api/messages/v3/delete` | Route literal needing call-site confirmation. |
| misc | `api/objectives/` | Route literal needing call-site confirmation. |
| misc | `api/PlayerCheer/` | Route literal needing call-site confirmation. |
| misc | `api/PlayerElo/` | Route literal needing call-site confirmation. |
| misc | `api/playerevents/` | Route literal needing call-site confirmation. |
| misc | `api/PlayerReporting/` | Route literal needing call-site confirmation. |
| misc | `api/PlayersBanned/` | Route literal needing call-site confirmation. |
| misc | `api/purchase/v1/cancelpurchase` | Route literal needing call-site confirmation. |
| misc | `api/purchase/v1/cleanuppending` | Route literal needing call-site confirmation. |
| misc | `api/purchase/v1/completepurchase` | Route literal needing call-site confirmation. |
| misc | `api/purchase/v1/initiatepurchase` | Route literal needing call-site confirmation. |
| misc | `api/purchase/v1/processpurchase` | Route literal needing call-site confirmation. |
| misc | `api/quickPlay/` | Route literal needing call-site confirmation. |
| misc | `api/relationships/` | Route literal needing call-site confirmation. |
| misc | `api/royale/` | Route literal needing call-site confirmation. |
| misc | `api/sanitize/` | Route literal needing call-site confirmation. |
| misc | `api/sanitize/v1` | Route literal needing call-site confirmation. |
| misc | `api/settings/` | Route literal needing call-site confirmation. |
| misc | `api/storefronts/` | Route literal needing call-site confirmation. |
| misc | `api/testcasemanagement/` | Route literal needing call-site confirmation. |
| misc | `api/versioncheck/v4?v={0}&p={1}` | Route literal needing call-site confirmation. |
| misc | `goto/event/{0}` | Route literal needing call-site confirmation. |
| misc | `goto/instance/{0}` | Route literal needing call-site confirmation. |
| misc | `goto/invite/{0}` | Route literal needing call-site confirmation. |
| misc | `goto/player/{0}` | Route literal needing call-site confirmation. |
| misc | `goto/room/` | Route literal needing call-site confirmation. |
| misc | `https://www.instagram.com/recroom/` | Route literal needing call-site confirmation. |
| misc | `room/{0}/instances` | Route literal needing call-site confirmation. |
| players | `/api/playerReputation/v1/{0}` | Player progression/reputation DTO or bulk list/map. |
| players | `/api/playerReputation/v1/bulk` | Player progression/reputation DTO or bulk list/map. |
| players | `/api/players/v1/progression/{0}` | Player progression/reputation DTO or bulk list/map. |
| players | `/api/players/v1/progression/bulk` | Player progression/reputation DTO or bulk list/map. |
| players | `api/players/v2/objectives` | Player progression/reputation DTO or bulk list/map. |
| players | `api/playersubscriptions/` | Player progression/reputation DTO or bulk list/map. |
| rooms | `api/rooms/` | Room detail/list/save-data response or room data descriptor. |
| rooms | `api/rooms/v1/roomRolePermissions` | Room detail/list/save-data response or room data descriptor. |
| rooms | `api/rooms/v4/saveData` | Room detail/list/save-data response or room data descriptor. |
| storage-cdn | `{0}v1/datahistory/{1}` | Storage/CDN data descriptor or binary-addressing fragment. |
| storage-cdn | `{0}v1/datahistory/restore` | Storage/CDN data descriptor or binary-addressing fragment. |
| storage-cdn | `/data/{0}` | Storage/CDN data descriptor or binary-addressing fragment. |
