namespace OnlyFate.Database;

public static partial class ReportHtmlRenderer
{
 private const string ScreenLayoutCss="""
@media screen{
 body{font-size:16px;line-height:1.85;background:#E9EAE5;color:#454B50}
 main{max-width:1080px;margin:28px auto;padding:0;background:transparent}
 .screen-only{display:block!important}.screen-only.inline{display:inline!important}.print-only{display:none!important}
 .report-content{--ink:#173F36;--body:#454B50;--muted:#60686D;--gold:#AA8859;--line:#DDD8CD;color:var(--body)}
 .report-content h1,.report-content h2,.report-content h3{color:var(--ink)}
 .report-content h1{font-size:36px;margin:20px 0 30px;line-height:1.45}
 .report-content h2{font-size:30px;margin:4px 0 16px;line-height:1.5}
 .report-content h3{font:700 19px/1.6 "Noto Sans TC","Microsoft JhengHei",sans-serif;margin:0 0 12px}
 .report-content p{margin:10px 0}.report-content a{color:var(--ink);text-underline-offset:5px}
 .report-content a:hover{color:#735a35}.report-content a:focus-visible{outline:3px solid var(--gold);outline-offset:5px}
 .report-content section{padding:36px 44px;margin:0 0 24px;background:#FCFAF6;border:1px solid #DADDD4;scroll-margin-top:20px}
 .print-status{color:var(--muted);font-size:13px;border-bottom:1px solid var(--line);padding-bottom:12px}
 .reading-guide{padding:22px 26px;background:#F0EEE6;border-left:3px solid var(--gold);margin:28px 0}
 .reading-steps{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:20px}
 .reading-steps strong{display:block;font-size:16px;color:var(--ink)}.reading-steps a{display:inline-block;font-size:14px;margin-top:6px}
 .case-intro{padding:22px 0;border-block:1px solid var(--line)}.case-intro p{color:var(--muted)}
 .cover-direction{margin:28px 0}.cover-direction h2{font-size:24px}
 .cover-schemes{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:24px}
 .cover-schemes article{border-top:1px solid var(--gold);padding-top:20px}.cover-schemes h3{font-size:23px;margin:6px 0}.cover-schemes p{font-size:14px}
 .pillar-strip{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:16px;margin:24px 0}
 .pillar-strip div{padding:16px;background:#F0EEE6;text-align:center}.pillar-strip span{display:block;font-size:14px;color:#735a35}
 .pillar-strip strong{display:block;font:600 32px/1.5 "Noto Serif TC","PMingLiU",serif;color:var(--ink);margin-top:6px}
 .analysis-note{padding:24px 0;border-top:1px solid var(--line)}.analysis-note p,.analysis-conclusion p{max-width:46em}
 .analysis-conclusion{padding:22px 26px;margin-top:12px;background:#F0EEE6;border-left:3px solid var(--gold)}
 .analysis-conclusion h3{font-size:22px}.analysis-limit{font-size:14px;color:var(--muted);margin-top:22px!important}
 .proposal-direction{font-size:18px;margin-bottom:28px!important}
 .compare-primary{display:grid;grid-template-columns:minmax(150px,1fr) 2fr;gap:24px;align-items:center;background:#EEF1EB;padding:26px 30px;margin:28px 0 32px}
 .example-meta{color:#735a35;font-size:14px;letter-spacing:.08em}.example-name{font:600 28px/1.5 "Noto Serif TC","PMingLiU",serif;color:var(--ink)}
 .compare-primary .example-name{font-size:38px;margin-top:8px}.primary-summary{font-weight:700;color:#30383C;margin-top:16px!important}
 .proposal-page>h3{border-left:3px solid var(--gold);padding-left:12px;margin-top:28px}
 .proposal-pools,.compare-alternatives{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:24px}
 .proposal-pools{margin:22px 0 32px}.proposal-pools article{border-top:1px solid var(--gold);padding-top:16px}
 .proposal-pools h3{font-size:17px}.pool-element{font-size:14px;font-weight:400;color:var(--muted)}
 .pool-letterline{font:600 28px/1.6 "Noto Serif TC","PMingLiU",serif;color:var(--ink);margin:14px 0!important}
 .compare-example{border-top:1px solid var(--line);padding-top:18px}.pair-tone{color:var(--muted);font-size:14px}.next-page-hint{margin-top:28px!important;font-size:14px}
 .comparison-heading{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:24px;align-items:center;border-bottom:2px solid var(--gold);padding-bottom:18px;margin-bottom:12px}
 .comparison-heading h2{margin-bottom:0}.comparison-pick{background:var(--ink);color:#FCFAF6;padding:12px 24px;min-width:152px;text-align:center}
 .comparison-pick span{display:block;color:#EEE1CC;font-size:13px;letter-spacing:.08em}.comparison-pick strong{display:block;font:600 26px/1.5 "Noto Serif TC","PMingLiU",serif}
 .comparison-guide,.compare-note{font-size:14px;color:var(--muted)}
 .comparison-columns,.comparison-row{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:20px}
 .comparison-columns{margin:24px 0 12px;font-weight:700;color:#735a35}.comparison-row{margin-bottom:18px}
 .compare-character{padding:20px 22px;border:1px solid var(--line);background:#FFFDF9;min-width:0}.compare-character.last{background:#F4F6F4;border-color:#CBD6D0}
 .compare-heading{display:flex;align-items:baseline;flex-wrap:wrap;gap:10px;border-bottom:1px solid var(--line);padding-bottom:12px;margin-bottom:12px}
 .compare-glyph{font:600 36px/1.3 "Noto Serif TC","PMingLiU",serif;color:var(--ink)}.compare-reading{font-size:15px}.compare-style{font-size:14px;color:var(--muted)}
 .pick-tag{margin-left:auto;color:var(--ink);background:#E4EBE3;border:1px solid #B6C6B9;border-radius:3px;padding:1px 7px;font-size:12px;font-weight:700}
 .compare-fact{display:grid;grid-template-columns:4.3em minmax(0,1fr);gap:10px;margin-top:10px}.compare-fact strong{font-size:14px;color:#735a35;font-weight:600}.compare-fact span{overflow-wrap:anywhere}
 .character-position.screen-only{display:none!important;font-size:13px;color:#735a35;margin-bottom:8px}.compare-note{margin-top:22px!important}
 @media(max-width:900px){main{margin:16px}.report-content section{padding:28px}.compare-character{padding:16px}.compare-fact{display:block}.compare-fact strong{display:block;margin-bottom:3px}.compare-primary{grid-template-columns:minmax(140px,1fr) 2fr;padding:24px}.reading-steps{gap:12px}}
 @media(max-width:620px){main{margin:0}.report-content section{padding:24px 20px;border-left:0;border-right:0;margin-bottom:14px}.report-content h1{font-size:30px}.report-content h2{font-size:26px}.reading-steps,.cover-schemes,.proposal-pools,.compare-alternatives,.compare-primary{grid-template-columns:1fr}.reading-steps{gap:18px}.reading-guide{padding:18px}.pillar-strip{gap:8px}.pillar-strip div{padding:12px 4px}.pillar-strip strong{font-size:25px}.comparison-heading{gap:12px}.comparison-heading h2{font-size:24px}.comparison-pick{min-width:118px;padding:10px 12px}.comparison-pick strong{font-size:22px}.comparison-row{grid-template-columns:1fr;gap:12px;margin-bottom:24px}.comparison-columns{display:none}.character-position.screen-only{display:block!important}.compare-fact{display:grid}.compare-primary .example-name{font-size:34px}.compare-primary{gap:12px}.proposal-pools{gap:22px}.analysis-conclusion{padding:20px}.comparison-guide{margin-bottom:20px!important}}
}
""";
}
