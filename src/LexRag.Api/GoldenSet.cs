using LexRag.Eval;

namespace LexRag.Api;

// In-domain cases must ground + cite the right súmula; out-of-domain must refuse (anti-hallucination gate).
// Cases may require multiple sources (multi-hop); recall measures coverage across all expected sources.
//
// Near-domain adversarial cases (InDomain:false) are legally-phrased questions that sound corpus-relevant
// but are NOT answerable from these five súmulas. A well-behaved system must refuse, not improvise.
public static class GoldenSet
{
    public static IReadOnlyList<EvalCase> Cases =>
    [
        // --- in-domain: SV 11 (algemas) ---
        new("uso de algemas exige resistência ou receio de fuga?",
            InDomain: true, ExpectedSourceFiles: ["stf-sv11-algemas.txt"]),
        new("quais são os requisitos para o uso legal de algemas segundo o STF?",
            InDomain: true, ExpectedSourceFiles: ["stf-sv11-algemas.txt"]),
        new("a excepcionalidade do uso de algemas deve ser justificada por escrito?",
            InDomain: true, ExpectedSourceFiles: ["stf-sv11-algemas.txt"]),

        // --- in-domain: SV 13 (nepotismo) ---
        new("nomeação de parente para cargo em comissão configura nepotismo?",
            InDomain: true, ExpectedSourceFiles: ["stf-sv13-nepotismo.txt"]),
        new("até qual grau de parentesco a SV 13 proíbe nomeações para cargo em comissão?",
            InDomain: true, ExpectedSourceFiles: ["stf-sv13-nepotismo.txt"]),
        new("designações recíprocas entre servidores podem configurar nepotismo?",
            InDomain: true, ExpectedSourceFiles: ["stf-sv13-nepotismo.txt"]),

        // --- in-domain: SV 25 (depositário infiel) ---
        new("é lícita a prisão civil de depositário infiel?",
            InDomain: true, ExpectedSourceFiles: ["stf-sv25-depositario-infiel.txt"]),
        new("a modalidade do depósito afeta a ilicitude da prisão civil do depositário infiel?",
            InDomain: true, ExpectedSourceFiles: ["stf-sv25-depositario-infiel.txt"]),

        // --- in-domain: Súmula 314 STJ (execução fiscal) ---
        new("prescrição intercorrente em execução fiscal quando não localizados bens penhoráveis",
            InDomain: true, ExpectedSourceFiles: ["stj-sumula314-execucao-fiscal.txt"]),
        new("por quanto tempo o processo é suspenso na execução fiscal sem bens penhoráveis?",
            InDomain: true, ExpectedSourceFiles: ["stj-sumula314-execucao-fiscal.txt"]),
        new("qual é o prazo da prescrição intercorrente na execução fiscal conforme a Súmula 314 do STJ?",
            InDomain: true, ExpectedSourceFiles: ["stj-sumula314-execucao-fiscal.txt"]),

        // --- in-domain: Súmula 7 STJ (reexame de prova) ---
        new("simples reexame de prova enseja recurso especial?",
            InDomain: true, ExpectedSourceFiles: ["stj-sumula7-reexame-prova.txt"]),
        new("a pretensão de reexame de prova em recurso especial é admitida pelo STJ?",
            InDomain: true, ExpectedSourceFiles: ["stj-sumula7-reexame-prova.txt"]),

        // --- multi-hop ---
        new("O STF admite o uso de algemas como regra e é lícita a prisão civil do depositário infiel?",
            InDomain: true, ExpectedSourceFiles: ["stf-sv11-algemas.txt", "stf-sv25-depositario-infiel.txt"]),
        new("Em recurso especial cabe o simples reexame de prova e qual o termo inicial da prescrição intercorrente na execução fiscal?",
            InDomain: true, ExpectedSourceFiles: ["stj-sumula7-reexame-prova.txt", "stj-sumula314-execucao-fiscal.txt"]),

        // --- trivial out-of-domain ---
        new("Qual a capital da Austrália?", InDomain: false),
        new("Quantos gols o Pelé marcou na carreira?", InDomain: false),

        // --- near-domain adversarial: legally-phrased but not answerable from these five súmulas ---
        // The STF has súmulas on wiretapping (SV 14, 56) but none are in this corpus.
        new("interceptação telefônica sem autorização judicial é admissível em investigação criminal?",
            InDomain: false),
        // Habeas corpus scope is a distinct STJ/STF line not covered by any of the five corpus files.
        new("cabe habeas corpus para discutir execução de pena privativa de liberdade já transitada em julgado?",
            InDomain: false),
        // Contract penalty clause limits are civil-code doctrine, not in the corpus.
        new("qual o limite legal da cláusula penal em contratos civis por inadimplemento parcial?",
            InDomain: false),
        // Administrative disqualification (improbidade) law has its own corpus; not covered here.
        new("a improbidade administrativa exige necessariamente dolo do agente público para sua configuração?",
            InDomain: false),
        // Consumer law cooling-off period is CDC doctrine; not addressed by any of the five súmulas.
        new("qual o prazo para exercício do direito de arrependimento em contratos celebrados fora do estabelecimento?",
            InDomain: false),
        // Employer social security contributions during paid leave is labor/tax law; not in corpus.
        new("o empregador deve recolher contribuições previdenciárias sobre os períodos de afastamento remunerado?",
            InDomain: false),
        // Statute of limitations for moral damages in labor suits — distinct STJ line not in corpus.
        new("qual é o prazo prescricional para reclamar indenização por danos morais decorrentes de relação de trabalho?",
            InDomain: false),
    ];
}
