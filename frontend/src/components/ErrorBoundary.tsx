import { Component, type ErrorInfo, type ReactNode } from 'react';

interface Props {
  children: ReactNode;
}

interface State {
  error: Error | null;
}

/**
 * Catches render errors in the component tree below it and shows a recoverable
 * fallback instead of a blank screen. API and action errors are handled locally
 * with toasts; this is the last line of defence for unexpected render faults.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { error: null };

  static getDerivedStateFromError(error: Error): State {
    return { error };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Surface to the console for local diagnosis; no data leaves the browser.
    console.error('Unhandled UI error', error, info.componentStack);
  }

  private handleReload = () => {
    this.setState({ error: null });
    window.location.reload();
  };

  render() {
    if (this.state.error) {
      return (
        <div className="page">
          <div className="card error-boundary" role="alert">
            <h1>Something went wrong</h1>
            <p className="muted">
              An unexpected error interrupted this page. Your data has not been changed.
            </p>
            <div className="action-bar">
              <button type="button" className="btn btn-primary" onClick={this.handleReload}>
                Reload
              </button>
            </div>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}
