import { Link } from 'react-router-dom';
import { Icon } from '../components/icons';
import type { IconName } from '../components/icons';

const PRODUCTS: {
  code: string;
  name: string;
  description: string;
  icon: IconName;
  meta: string;
}[] = [
  {
    code: 'SAV-001',
    name: 'Everyday Savings',
    description: 'A simple savings account journey with transparent, demonstration-only terms.',
    icon: 'wallet',
    meta: 'Digital onboarding',
  },
  {
    code: 'CC-001',
    name: 'Everyday Credit Card',
    description: 'Explore card eligibility, documents and issue resolution in one focused flow.',
    icon: 'file',
    meta: 'Cards',
  },
  {
    code: 'PERS-001',
    name: 'Personal Loan',
    description: 'Apply, upload documents and track decisions without any real credit check.',
    icon: 'briefcase',
    meta: 'Loans',
  },
  {
    code: 'FD-001',
    name: 'Fixed Deposit',
    description: 'Review a fixed-term product with clearly labelled illustrative terms.',
    icon: 'trendingUp',
    meta: 'Deposits',
  },
];

const STEPS = [
  {
    title: 'Choose a product',
    body: 'Browse a focused catalogue and review eligibility, documents and demo terms before you begin.',
  },
  {
    title: 'Complete your profile',
    body: 'Add contact and address details, then upload sample KYC documents through the secure upload flow.',
  },
  {
    title: 'Track every decision',
    body: 'Follow status history, respond to information requests and see auditable reviewer decisions.',
  },
  {
    title: 'Get grounded support',
    body: 'Agents triage conversations with approved knowledge articles and advisory next best actions.',
  },
];

export function LandingPage() {
  return (
    <div className="landing">
      <a className="skip-link" href="#landing-main">
        Skip to content
      </a>

      <header className="landing-header">
        <div className="landing-container landing-header-inner">
          <Link to="/" className="landing-brand" aria-label="Northstar home">
            <span className="brand-mark" aria-hidden="true">
              <Icon name="star" size={20} />
            </span>
            <span>
              <strong>Northstar</strong>
              <small>Banking workspace</small>
            </span>
          </Link>
          <nav aria-label="Landing navigation">
            <a href="#products">Products</a>
            <a href="#how-it-works">How it works</a>
            <a href="#trust">Security</a>
          </nav>
          <Link to="/login" className="btn btn-primary btn-sm">
            Sign in
            <Icon name="arrowRight" size={15} />
          </Link>
        </div>
      </header>

      <main id="landing-main">
        <section className="landing-hero" aria-labelledby="landing-title">
          <div className="landing-container landing-hero-grid">
            <div className="landing-hero-copy">
              <p className="landing-eyebrow">Digital banking, designed for clarity</p>
              <h1 id="landing-title">
                Banking that keeps you <span>in the loop.</span>
              </h1>
              <p className="landing-lede">
                Northstar brings applications, KYC, decisions and support into one calm, role-aware
                workspace. Explore a realistic banking journey with clearly labelled demonstration
                data — no real money, credit checks or account changes.
              </p>
              <div className="landing-cta-row">
                <Link to="/login" className="btn btn-primary btn-lg">
                  Enter the workspace
                  <Icon name="arrowRight" size={17} />
                </Link>
                <a href="#how-it-works" className="btn btn-ghost btn-lg landing-ghost">
                  See how it works
                </a>
              </div>
              <ul className="landing-assurances" aria-label="Demo assurances">
                <li>
                  <Icon name="shieldCheck" size={15} /> Role-based access
                </li>
                <li>
                  <Icon name="lock" size={15} /> Masked identifiers
                </li>
                <li>
                  <Icon name="checkCircle" size={15} /> Auditable decisions
                </li>
              </ul>
            </div>

            <div className="landing-hero-panel" aria-label="Demo workspace preview">
              <div className="landing-panel-top">
                <span className="landing-panel-dots" aria-hidden="true">
                  <i /> <i /> <i />
                </span>
                <span>northstar / workspace</span>
              </div>
              <div className="landing-panel-body">
                <div className="landing-panel-heading">
                  <div>
                    <span className="muted text-xs">TODAY</span>
                    <strong>Good morning, Aarav</strong>
                  </div>
                  <span className="badge badge--success">KYC verified</span>
                </div>
                <div className="landing-balance-card">
                  <span>Available balance · demo</span>
                  <strong>₹84,250.75</strong>
                  <small>•••• •••• 4831 · NSTD0•••48</small>
                </div>
                <div className="landing-panel-rows">
                  <div>
                    <span>Everyday Savings</span>
                    <span className="badge badge--info">Under review</span>
                  </div>
                  <div>
                    <span>Card charge enquiry</span>
                    <span className="badge badge--warning">Open</span>
                  </div>
                  <div>
                    <span>Fixed Deposit</span>
                    <span className="badge badge--success">Approved</span>
                  </div>
                </div>
                <div className="landing-panel-note">
                  <Icon name="sparkles" size={14} />
                  AI guidance is advisory. A human always confirms the next step.
                </div>
              </div>
            </div>
          </div>
        </section>

        <section className="landing-strip" aria-label="Platform highlights">
          <div className="landing-container landing-strip-grid">
            <div>
              <strong>4</strong>
              <span>focused banking products</span>
            </div>
            <div>
              <strong>26</strong>
              <span>approved support articles</span>
            </div>
            <div>
              <strong>3</strong>
              <span>agent-assisted workflows</span>
            </div>
            <div>
              <strong>100%</strong>
              <span>illustrative demo data</span>
            </div>
          </div>
        </section>

        <section id="products" className="landing-section" aria-labelledby="products-title">
          <div className="landing-container">
            <div className="landing-section-head">
              <div>
                <p className="landing-eyebrow">Products</p>
                <h2 id="products-title">A focused set of everyday banking journeys</h2>
              </div>
              <p>
                Each product uses the same onboarding pipeline, so reviewers and support agents see a
                consistent, explainable state model.
              </p>
            </div>
            <div className="landing-product-grid">
              {PRODUCTS.map((product) => (
                <article className="landing-product" key={product.code}>
                  <span className="landing-product-icon" aria-hidden="true">
                    <Icon name={product.icon} size={20} />
                  </span>
                  <span className="landing-product-meta">{product.meta}</span>
                  <h3>{product.name}</h3>
                  <p>{product.description}</p>
                  <code>{product.code}</code>
                </article>
              ))}
            </div>
          </div>
        </section>

        <section id="how-it-works" className="landing-section landing-section--tint" aria-labelledby="steps-title">
          <div className="landing-container">
            <div className="landing-section-head">
              <div>
                <p className="landing-eyebrow">How it works</p>
                <h2 id="steps-title">From application to resolution, one clear path</h2>
              </div>
              <p>
                The same auditable state model powers customer tracking, reviewer decisions and the AI
                support workspace.
              </p>
            </div>
            <ol className="landing-steps">
              {STEPS.map((step, index) => (
                <li key={step.title}>
                  <span className="landing-step-number">{index + 1}</span>
                  <h3>{step.title}</h3>
                  <p>{step.body}</p>
                </li>
              ))}
            </ol>
          </div>
        </section>

        <section id="trust" className="landing-section" aria-labelledby="trust-title">
          <div className="landing-container landing-trust-grid">
            <div>
              <p className="landing-eyebrow">Safety by design</p>
              <h2 id="trust-title">Assistance you can question, actions you control</h2>
              <p className="landing-lede">
                The copilot never approves applications, changes permissions or performs account actions.
                Recommendations show their evidence, approved article sources and expected outcome, and
                sensitive steps stay behind explicit human confirmation.
              </p>
              <Link to="/login" className="link-button">
                Explore the support workspace <Icon name="arrowRight" size={14} />
              </Link>
            </div>
            <ul className="landing-trust-list">
              <li>
                <Icon name="file" size={17} />
                <div>
                  <strong>Grounded answers</strong>
                  <span>Every recommendation cites an approved knowledge article.</span>
                </div>
              </li>
              <li>
                <Icon name="shieldCheck" size={17} />
                <div>
                  <strong>Advisory only</strong>
                  <span>Generated content is labelled as a development fallback or live inference.</span>
                </div>
              </li>
              <li>
                <Icon name="lock" size={17} />
                <div>
                  <strong>Protected context</strong>
                  <span>No full account numbers, credentials or document links are exposed.</span>
                </div>
              </li>
            </ul>
          </div>
        </section>
      </main>

      <footer className="landing-footer">
        <div className="landing-container landing-footer-inner">
          <div>
            <strong>Northstar</strong>
            <p>Demonstration banking workspace. Not a financial institution or a real banking service.</p>
          </div>
          <nav aria-label="Footer navigation">
            <a href="#products">Products</a>
            <a href="#how-it-works">How it works</a>
            <a href="#trust">Security</a>
            <Link to="/login">Sign in</Link>
          </nav>
        </div>
      </footer>
    </div>
  );
}
