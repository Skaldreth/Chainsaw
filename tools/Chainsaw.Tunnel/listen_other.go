//go:build !windows

package main

import (
	"context"
	"net"
)

// listenLoopback listens on 127.0.0.1 with a free port. Outside Windows a port that is in use cannot be bound by
// another program unless both ask for it, so nothing more is needed; the helper ships for Windows only, and this
// file lets the tests run elsewhere.
func listenLoopback(ctx context.Context) (net.Listener, error) {
	var config net.ListenConfig
	return config.Listen(ctx, "tcp4", "127.0.0.1:0")
}
